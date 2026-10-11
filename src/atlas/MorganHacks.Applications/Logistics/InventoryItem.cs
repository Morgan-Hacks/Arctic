using MorganHacks.Applications.Services;

namespace MorganHacks.Applications.Logistics;

/// <summary>
/// One thing logistics is holding, as the console's list and detail screens
/// need it.
/// </summary>
/// <remarks>
/// <see cref="IsLow"/> is computed here rather than left for the console to
/// derive from <see cref="Count"/> and <see cref="LowAt"/>. The rule —
/// <c>lowAt is not null &amp;&amp; count &lt;= lowAt</c> — is one sentence
/// today, but it is the kind of sentence that grows a second clause once
/// somebody asks for a warning that clears itself a few units early, and a
/// rule written in two places drifts in one of them.
/// <para>
/// <see cref="ImageUrl"/> is always null for now. The column it will read
/// from, <c>items.image_key</c>, already exists — see <c>0051_inventory</c> —
/// but minting the signed link it stands for is separate work. The field is
/// here so the console can build against its final shape without a second
/// contract change once that work lands.
/// </para>
/// </remarks>
public sealed record InventoryItemView(
    Guid Id,
    string Name,
    string? Category,
    string? Location,
    string? Note,
    int? LowAt,
    int Count,
    bool IsLow,
    string? ImageUrl,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// The blob this item's photo lives under, never serialised.
    /// </summary>
    /// <remarks>
    /// The store fills this in and the endpoint turns it into the signed
    /// <see cref="ImageUrl"/>. JsonIgnore rather than discipline: a key that
    /// reached the console would be useless to it -- the container is not
    /// public -- and would sit in the one field a browser is willing to put in
    /// an img tag, so a forgotten signing step would show as a broken image
    /// rather than as an error. The attribute makes forgetting it impossible.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ImageKey { get; init; }
}

/// <summary>
/// One line of the ledger, as the console's history screen needs it.
/// </summary>
/// <remarks>
/// <see cref="Actor"/> is a name or an email, never an id — the console has
/// no other screen to resolve a person against here, unlike the audit trail,
/// which deliberately leaves that join to <c>/admin/people</c>. Null is a
/// real answer and means the person has since been removed; see
/// <c>logistics.movements.actor_id</c>, which is set null on delete rather
/// than cascaded, so the row survives the person.
/// </remarks>
public sealed record InventoryMovementView(
    long Id,
    int Delta,
    int Balance,
    string? Reason,
    string? Actor,
    DateTimeOffset OccurredAt);

/// <summary>
/// What adding an item takes.
/// </summary>
/// <remarks>
/// <see cref="Count"/> defaults to zero rather than being required, because
/// the common case — a new line on the sheet before anything has arrived — is
/// one nobody should have to spell out as "0". A nonzero starting count is
/// written through <see cref="IInventoryStore.CreateAsync"/> as an opening
/// movement in the same transaction as the row, so the ledger explains the
/// number from the start rather than beginning with forty shirts nobody
/// recorded taking in.
/// </remarks>
public sealed record NewInventoryItem(
    string Name,
    string? Category,
    string? Location,
    string? Note,
    int? LowAt,
    int Count);

/// <summary>
/// A text field a PATCH either carries or leaves alone.
/// </summary>
/// <remarks>
/// The companion to <see cref="Patch{T}"/> for the columns that type cannot
/// hold — it is constrained to a struct, and a column here is nullable text.
/// The same three-state reasoning applies: an update naming only
/// <c>location</c> must not clear <c>category</c> and <c>note</c> alongside
/// it, and an update sending <c>"category": null</c> has to mean "this no
/// longer has one" rather than "unchanged".
/// </remarks>
public readonly record struct TextPatch(bool Present, string? Value)
{
    public static TextPatch To(string? value) => new(true, value);
}

/// <summary>
/// What a PATCH to an item carries.
/// </summary>
/// <remarks>
/// <see cref="Name"/> is a plain nullable rather than a <see cref="TextPatch"/>
/// for the same reason <c>EventEdit.Name</c> is: the column is NOT NULL, so
/// there is no clearing it, only correcting it. Everything else here can be
/// unset, and so carries the three states a <see cref="TextPatch"/> or
/// <see cref="Patch{T}"/> is for.
/// <para>
/// <see cref="Count"/> is deliberately absent. The only way a count changes
/// is <see cref="IInventoryStore.MoveAsync"/>, which writes the ledger row
/// that explains the change in the same breath — an edit endpoint that could
/// also move the count would be a second, unexplained way to do the one thing
/// this whole module exists to keep explained.
/// </para>
/// </remarks>
public sealed record InventoryItemEdit
{
    public string? Name { get; init; }
    public TextPatch Category { get; init; }
    public TextPatch Location { get; init; }
    public TextPatch Note { get; init; }
    public Patch<int> LowAt { get; init; }
}

/// <summary>What a request to move the count can fail on, other than not existing.</summary>
public enum InventoryMoveOutcome
{
    Ok,
    NotFound,

    /// <summary>
    /// The item has fewer than the movement would take out. Refused before
    /// the write reaches <c>CHECK (count &gt;= 0)</c>, so the caller gets a
    /// sentence naming what is actually there rather than a constraint
    /// violation.
    /// </summary>
    InsufficientStock,
}

/// <summary>
/// The outcome of <see cref="IInventoryStore.MoveAsync"/>.
/// </summary>
/// <remarks>
/// <see cref="Item"/> is populated only on <see cref="InventoryMoveOutcome.Ok"/>.
/// The other two outcomes have nothing to show — there is no row for
/// <see cref="InventoryMoveOutcome.NotFound"/>, and refusing the write for
/// <see cref="InventoryMoveOutcome.InsufficientStock"/> means the row did not
/// change, so returning the stale one would read as if the movement had
/// happened.
/// </remarks>
public sealed record InventoryMovementResult(
    InventoryMoveOutcome Outcome, InventoryItemView? Item, string? Error);

/// <summary>What a delete removed, and what it left behind in storage.</summary>
/// <remarks>
/// <see cref="ImageKey"/> is the photo that is now unreferenced, or null. The
/// row is gone by the time this is returned, so the blob is deleted after --
/// an orphaned file costs nothing, and an item pointing at a file that is not
/// there is a broken image on the screen somebody counts boxes with.
/// </remarks>
public sealed record InventoryDeletion(bool Removed, string? ImageKey);

/// <summary>Where a new photo should go, and what is already there.</summary>
/// <remarks>
/// The event is needed before the upload, because it is part of the key -- a
/// season's photos sit under one prefix so they can be removed together. The
/// current key is needed after, so replacing a photo does not leave the old
/// one behind.
/// </remarks>
public sealed record InventoryImageTarget(Guid EventId, string? CurrentKey);

/// <summary>The result of pointing an item at a different photo.</summary>
public sealed record InventoryImageSwap(InventoryItemView Item, string? PreviousKey);

