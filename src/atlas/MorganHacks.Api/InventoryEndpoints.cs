using System.Text.Json;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Logistics;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;

namespace MorganHacks.Api;

/// <summary>
/// What logistics has, where it is, and how much of it is left — see
/// <c>0051_inventory</c> for the two tables this reads and writes.
/// </summary>
/// <remarks>
/// Every route here requires <c>inventory.manage</c>. There is no separate
/// read permission the way applications has <c>applications.view</c>
/// alongside <c>applications.decide</c> — see <see cref="Permission.InventoryManage"/>
/// for why a volunteer who can see the count and one who can correct it are
/// the same volunteer here.
/// <para>
/// The event is resolved the same way <c>ApplicantAnalyticsEndpoints</c>
/// resolves it: against the event list, 404 on an id that does not match
/// one, defaulting to the most recent. That resolution lives on
/// <see cref="List"/> and <see cref="Create"/> only — everything that names
/// an item by id (<see cref="Update"/>, <see cref="Delete"/>,
/// <see cref="Move"/>, <see cref="Movements"/>) has no event to resolve,
/// because the id already picks one row.
/// </para>
/// </remarks>
public static class InventoryEndpoints
{
    public static IEndpointRouteBuilder MapInventory(this IEndpointRouteBuilder app)
    {
        var inventory = app.MapGroup("/admin/inventory");

        inventory.MapGet("", List)
                 .RequirePermission(Permission.InventoryManage);
        inventory.MapPost("", Create)
                 .RequirePermission(Permission.InventoryManage);
        inventory.MapPatch("/{id:guid}", Update)
                 .RequirePermission(Permission.InventoryManage);
        inventory.MapDelete("/{id:guid}", Delete)
                 .RequirePermission(Permission.InventoryManage);
        inventory.MapPost("/{id:guid}/movement", Move)
                 .RequirePermission(Permission.InventoryManage);
        inventory.MapGet("/{id:guid}/movements", Movements)
                 .RequirePermission(Permission.InventoryManage);

        // A photo is replaced by posting a new one and removed by deleting it,
        // rather than being a field on PATCH. A multipart body and a JSON patch
        // do not share a shape, and a PATCH that sometimes carried a file would
        // be two endpoints wearing one name.
        inventory.MapPost("/{id:guid}/image", SetImage)
                 .RequirePermission(Permission.InventoryManage)
                 .DisableAntiforgery();
        inventory.MapDelete("/{id:guid}/image", ClearImage)
                 .RequirePermission(Permission.InventoryManage);

        return app;
    }

    // ------------------------------------------------------------ requests ---

    /// <summary>
    /// What adding an item takes.
    /// </summary>
    /// <remarks>
    /// Nullable throughout for the reason <c>CreateEventRequest</c> is:
    /// minimal APIs bind the body before the permission gate runs, so a
    /// required body would answer a request the gate has not looked at yet.
    /// </remarks>
    public sealed record CreateInventoryItemRequest(
        string? Name, string? Category, string? Location, string? Note, int? LowAt, int? Count);

    /// <summary>
    /// What a PATCH carries, as raw JSON rather than typed fields.
    /// </summary>
    /// <remarks>
    /// <see cref="JsonElement"/> for the reason <c>UpdateEventRequest</c>
    /// uses it: every field here has three states — absent, explicitly
    /// cleared, and set — and a plain nullable can only tell two of them
    /// apart. An update naming only <c>location</c> must leave
    /// <c>category</c> and <c>note</c> exactly where they were, and an
    /// update sending <c>"category": null</c> has to clear it rather than be
    /// indistinguishable from not mentioning it at all.
    /// </remarks>
    public sealed record UpdateInventoryItemRequest(
        JsonElement Name, JsonElement Category, JsonElement Location, JsonElement Note, JsonElement LowAt);

    /// <summary>What recording a movement takes.</summary>
    public sealed record MovementRequest(int? Delta, string? Reason);

    // ------------------------------------------------------------- reading ---

    /// <summary>
    /// Every item for the chosen event, plus the event picker. Requires
    /// <c>inventory.manage</c>.
    /// </summary>
    private static async Task<IResult> List(
        IEventStore events, IInventoryStore inventory, IInventoryImageStore images,
        CancellationToken ct, Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        var items = chosen is null
            ? (IReadOnlyList<InventoryItemView>)[]
            : await inventory.ListAsync(chosen.Id, ct);

        return Results.Ok(new { events = all, chosen, items = await Signed(items, images, ct) });
    }

    /// <summary>
    /// The ledger for one item, newest first. Requires <c>inventory.manage</c>.
    /// </summary>
    private static async Task<IResult> Movements(Guid id, IInventoryStore inventory, CancellationToken ct)
    {
        var movements = await inventory.MovementsAsync(id, ct);
        return movements is null
            ? Results.NotFound(new { error = "No such item." })
            : Results.Ok(new { movements });
    }

    // ------------------------------------------------------------- writing ---

    /// <summary>
    /// Adds an item to the chosen event. Requires <c>inventory.manage</c>.
    /// </summary>
    /// <remarks>
    /// A nonzero starting count is written through
    /// <see cref="IInventoryStore.CreateAsync"/> as an opening movement, in
    /// the same transaction as the row. See <c>NewInventoryItem</c> for why:
    /// a tracker that can begin at forty with nothing in the ledger to
    /// explain it is the exact failure this module exists to prevent.
    /// </remarks>
    private static async Task<IResult> Create(
        CreateInventoryItemRequest? request,
        HttpContext http,
        IEventStore events,
        IInventoryStore inventory,
        ILogger<CreateInventoryItemRequest> log,
        CancellationToken ct,
        Guid? eventId = null)
    {
        var all = await events.ListAsync(ct);
        var chosen = eventId.HasValue ? all.FirstOrDefault(e => e.Id == eventId) : all.FirstOrDefault();
        if (eventId.HasValue && chosen is null)
        {
            return Results.NotFound(new { error = "No such event." });
        }

        if (chosen is null)
        {
            return Results.BadRequest(new { error = "Create an event before adding inventory." });
        }

        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return Results.BadRequest(new { error = "An item needs a name." });
        }

        if (name.Length > 120)
        {
            return Results.BadRequest(new { error = "A name is at most 120 characters." });
        }

        if (!NormalizeText(request?.Category, "A category", 60, out var category, out var refusal)
            || !NormalizeText(request?.Location, "A location", 120, out var location, out refusal)
            || !NormalizeText(request?.Note, "A note", 500, out var note, out refusal))
        {
            return Results.BadRequest(new { error = refusal });
        }

        if (request?.LowAt is { } lowAt && lowAt < 0)
        {
            return Results.BadRequest(new { error = "The low warning threshold cannot be negative." });
        }

        var count = request?.Count ?? 0;
        if (count < 0)
        {
            return Results.BadRequest(new { error = "A starting count cannot be negative." });
        }

        var created = await inventory.CreateAsync(
            chosen.Id,
            new NewInventoryItem(name, category, location, note, request?.LowAt, count),
            http.PersonId(),
            ct);

        log.LogInformation(
            "Inventory item created. {actor} {item_id} {event_id}", http.PersonId(), created.Id, chosen.Id);

        return Results.Created($"/admin/inventory/{created.Id}", new { item = created });
    }

    /// <summary>
    /// Renames or corrects an item. Requires <c>inventory.manage</c>.
    /// </summary>
    /// <remarks>
    /// Count is not here. The only way a count changes is <see cref="Move"/>,
    /// which writes the ledger row that explains the change in the same
    /// breath — see <c>InventoryItemEdit</c> for why this endpoint must not
    /// grow a second, unexplained way to do that.
    /// </remarks>
    private static async Task<IResult> Update(
        Guid id,
        UpdateInventoryItemRequest? request,
        HttpContext http,
        IInventoryStore inventory,
        ILogger<UpdateInventoryItemRequest> log,
        CancellationToken ct)
    {
        // An absent body is every field left alone, the same as a body that
        // named none of them — default(JsonElement) carries
        // JsonValueKind.Undefined, exactly what a field not sent arrives as.
        request ??= new UpdateInventoryItemRequest(default, default, default, default, default);

        string? name = null;
        if (request.Name.ValueKind is not JsonValueKind.Undefined)
        {
            if (request.Name.ValueKind is not JsonValueKind.String
                || string.IsNullOrWhiteSpace(request.Name.GetString()))
            {
                return Results.BadRequest(new { error = "An item needs a name." });
            }

            name = request.Name.GetString()!.Trim();
            if (name.Length > 120)
            {
                return Results.BadRequest(new { error = "A name is at most 120 characters." });
            }
        }

        if (!TryPatchText(request.Category, "A category", 60, out var category, out var refusal)
            || !TryPatchText(request.Location, "A location", 120, out var location, out refusal)
            || !TryPatchText(request.Note, "A note", 500, out var note, out refusal)
            || !TryLowAt(request.LowAt, out var lowAt, out refusal))
        {
            return Results.BadRequest(new { error = refusal });
        }

        var updated = await inventory.UpdateAsync(id, new InventoryItemEdit
        {
            Name = name,
            Category = category,
            Location = location,
            Note = note,
            LowAt = lowAt,
        }, ct);

        if (updated is null)
        {
            return Results.NotFound(new { error = "No such item." });
        }

        log.LogInformation("Inventory item updated. {actor} {item_id}", http.PersonId(), id);

        return Results.Ok(new { item = updated });
    }

    /// <summary>Removes an item, its ledger and its photo. Requires <c>inventory.manage</c>.</summary>
    /// <remarks>
    /// The row goes first and the blob after, which is the safe order rather
    /// than the tidy one. A photo deleted before a row delete that then failed
    /// would leave an item pointing at a file that is not there -- a broken
    /// image on the screen somebody counts boxes with. The other way round
    /// leaves a file nobody references, which costs a fraction of a penny.
    /// <para>
    /// A failure to remove the blob is logged and swallowed. The item is gone,
    /// which is what was asked for; turning a successful delete into a 500
    /// because the cleanup failed would leave the caller believing the item is
    /// still there.
    /// </para>
    /// </remarks>
    private static async Task<IResult> Delete(
        Guid id, HttpContext http, IInventoryStore inventory, IInventoryImageStore images,
        ILogger<UpdateInventoryItemRequest> log, CancellationToken ct)
    {
        var removed = await inventory.DeleteAsync(id, ct);
        if (!removed.Removed)
        {
            return Results.NotFound(new { error = "No such item." });
        }

        if (removed.ImageKey is { } key)
        {
            try
            {
                await images.RemoveAsync(key, ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex,
                    "Could not remove the photo of a deleted item. {item_id}", id);
            }
        }

        log.LogInformation("Inventory item deleted. {actor} {item_id}", http.PersonId(), id);

        return Results.NoContent();
    }

    /// <summary>
    /// Records a movement: taking out, putting back, or correcting a count.
    /// Requires <c>inventory.manage</c>.
    /// </summary>
    /// <remarks>
    /// The only failure modes a caller can act on are "no such item" and
    /// "not enough there" — see <see cref="InventoryMoveOutcome"/> — and both
    /// come back as sentences from <see cref="IInventoryStore.MoveAsync"/>
    /// rather than as a 500 from <c>CHECK (count &gt;= 0)</c>.
    /// </remarks>
    private static async Task<IResult> Move(
        Guid id,
        MovementRequest? request,
        HttpContext http,
        IInventoryStore inventory,
        ILogger<MovementRequest> log,
        CancellationToken ct)
    {
        if (request?.Delta is not { } delta || delta == 0)
        {
            return Results.BadRequest(new
            {
                error = "A movement needs a nonzero amount: negative to take out, positive to put back.",
            });
        }

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason is { Length: > 200 })
        {
            return Results.BadRequest(new { error = "A reason is at most 200 characters." });
        }

        var result = await inventory.MoveAsync(id, delta, reason, http.PersonId(), ct);

        switch (result.Outcome)
        {
            case InventoryMoveOutcome.NotFound:
                return Results.NotFound(new { error = "No such item." });

            case InventoryMoveOutcome.InsufficientStock:
                return Results.BadRequest(new { error = result.Error });

            default:
                log.LogInformation(
                    "Inventory movement recorded. {actor} {item_id} {delta}", http.PersonId(), id, delta);
                return Results.Ok(new { item = result.Item });
        }
    }

    // --------------------------------------------------------- reading a field ---

    /// <summary>Trims a plain optional string from a create request, or refuses it.</summary>
    private static bool NormalizeText(
        string? text, string label, int maxLength, out string? value, out string? error)
    {
        error = null;
        value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (value is not null && value.Length > maxLength)
        {
            error = $"{label} is at most {maxLength} characters.";
            return false;
        }

        return true;
    }

    /// <summary>Reads a text field from a PATCH: absent, cleared, or set.</summary>
    private static bool TryPatchText(
        JsonElement field, string label, int maxLength, out TextPatch patch, out string? error)
    {
        patch = default;
        error = null;

        switch (field.ValueKind)
        {
            case JsonValueKind.Undefined:
                return true;

            case JsonValueKind.Null:
                patch = TextPatch.To(null);
                return true;

            case JsonValueKind.String:
                var trimmed = string.IsNullOrWhiteSpace(field.GetString()) ? null : field.GetString()!.Trim();
                if (trimmed is not null && trimmed.Length > maxLength)
                {
                    error = $"{label} is at most {maxLength} characters.";
                    return false;
                }

                patch = TextPatch.To(trimmed);
                return true;

            default:
                error = $"{label} must be text.";
                return false;
        }
    }

    /// <summary>Reads the low warning threshold from a PATCH: absent, cleared, or a count.</summary>
    private static bool TryLowAt(JsonElement field, out Patch<int> patch, out string? error)
    {
        patch = default;
        error = null;

        switch (field.ValueKind)
        {
            case JsonValueKind.Undefined:
                return true;

            case JsonValueKind.Null:
                patch = Patch<int>.To(null);
                return true;

            case JsonValueKind.Number when field.TryGetInt32(out var value):
                if (value < 0)
                {
                    error = "The low warning threshold cannot be negative.";
                    return false;
                }

                patch = Patch<int>.To(value);
                return true;

            default:
                error = "The low warning threshold must be a whole number.";
                return false;
        }
    }

    /// <summary>Swaps each item's storage key for a link a browser can follow.</summary>
    /// <remarks>
    /// Done here rather than in the store because signing is a call to storage,
    /// and a store that reached for it would turn listing forty items into
    /// forty round trips before the first one drew. Done at all because the
    /// container is not public: what the console can use is a signed link, and
    /// it expires, so it has to be minted per request rather than stored.
    /// <para>
    /// A photo that cannot be signed comes back as an item without one. The
    /// screen exists to count boxes, and a storage account having a bad day
    /// should cost the picture rather than the page.
    /// </para>
    /// </remarks>
    private static async Task<IReadOnlyList<InventoryItemView>> Signed(
        IReadOnlyList<InventoryItemView> items, IInventoryImageStore images, CancellationToken ct)
    {
        if (!images.Available || items.All(i => i.ImageKey is null))
        {
            return items;
        }

        var signed = new List<InventoryItemView>(items.Count);
        foreach (var item in items)
        {
            signed.Add(await Signed(item, images, ct));
        }

        return signed;
    }

    private static async Task<InventoryItemView> Signed(
        InventoryItemView item, IInventoryImageStore images, CancellationToken ct)
    {
        if (!images.Available || item.ImageKey is not { } key)
        {
            return item;
        }

        try
        {
            var link = await images.LinkToAsync(key, ct);
            return link is null ? item : item with { ImageUrl = link.Url.ToString() };
        }
        catch (Exception)
        {
            return item;
        }
    }

    /// <summary>
    /// Replaces one item's photo. Requires <c>inventory.manage</c>.
    /// </summary>
    /// <remarks>
    /// The item is looked up before a byte is read, so an unknown id costs a
    /// query rather than four megabytes of upload. The order after that is
    /// store, point the row at it, then delete what it pointed at before --
    /// the same order the delete path uses, and for the same reason: a row
    /// pointing at a file that is not there is a broken image on screen, and a
    /// file nobody points at is a fraction of a penny.
    /// </remarks>
    private static async Task<IResult> SetImage(
        Guid id, HttpRequest request, HttpContext http,
        IInventoryStore inventory, IInventoryImageStore images,
        ILogger<UpdateInventoryItemRequest> log, CancellationToken ct)
    {
        if (!images.Available)
        {
            // COPY: needs sign-off.
            return Results.Json(
                new { error = "Photos are not set up in this environment." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (await inventory.ImageTargetAsync(id, ct) is not { } target)
        {
            return Results.NotFound(new { error = "No such item." });
        }

        var picked = await UploadedFile.ReadOneAsync(request, InventoryImage.MaxBytes, ct);
        if (picked is null)
        {
            // COPY: needs sign-off.
            return Results.BadRequest(new { error = "No photo arrived. Choose it again." });
        }

        var content = picked.Value.Content;
        if (content is null)
        {
            return Results.BadRequest(
                new { error = InventoryImage.Explain(InventoryImageRejection.TooLarge) });
        }

        var rejection = InventoryImage.Inspect(content, out var contentType);
        if (rejection != InventoryImageRejection.None)
        {
            return Results.BadRequest(new { error = InventoryImage.Explain(rejection) });
        }

        var key = await images.StoreAsync(target.EventId, id, content, contentType, ct);

        if (await inventory.SetImageAsync(id, key, ct) is not { } swap)
        {
            // Deleted between the lookup and now. Remove what was just stored
            // rather than leaving a photo of an item that no longer exists.
            await images.RemoveAsync(key, ct);
            return Results.NotFound(new { error = "No such item." });
        }

        await Forget(swap.PreviousKey, images, log, id, ct);

        log.LogInformation("Inventory photo set. {actor} {item_id}", http.PersonId(), id);

        return Results.Ok(new { item = await Signed(swap.Item, images, ct) });
    }

    /// <summary>Removes one item's photo. Requires <c>inventory.manage</c>.</summary>
    private static async Task<IResult> ClearImage(
        Guid id, HttpContext http, IInventoryStore inventory, IInventoryImageStore images,
        ILogger<UpdateInventoryItemRequest> log, CancellationToken ct)
    {
        if (await inventory.SetImageAsync(id, null, ct) is not { } swap)
        {
            return Results.NotFound(new { error = "No such item." });
        }

        await Forget(swap.PreviousKey, images, log, id, ct);

        log.LogInformation("Inventory photo cleared. {actor} {item_id}", http.PersonId(), id);

        return Results.Ok(new { item = swap.Item });
    }

    /// <summary>
    /// Deletes a photo nothing points at any more, and never fails for it.
    /// </summary>
    /// <remarks>
    /// The row already says what it should say by the time this runs. Turning
    /// a storage error into a failed request would tell somebody their photo
    /// did not change when it did.
    /// </remarks>
    private static async Task Forget(
        string? key, IInventoryImageStore images, ILogger log, Guid id, CancellationToken ct)
    {
        if (key is null)
        {
            return;
        }

        try
        {
            await images.RemoveAsync(key, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not remove a replaced inventory photo. {item_id}", id);
        }
    }
}
