using MorganHacks.Applications.Assistant;
using MorganHacks.Applications.Services;
using MorganHacks.Identity;
using MorganHacks.Identity.Domain;
using MorganHacks.Identity.Services;

namespace MorganHacks.Api;

/// <summary>
/// Asking the console a question, and asking it a follow-up.
/// </summary>
/// <remarks>
/// <para>
/// The assistant answers out of reads it is allowed to make. It holds no
/// connection and writes no query: it picks from a named list, each entry a
/// call into a store the console already calls, scoped to one event, returning
/// counted-up figures rather than people. The model chooses which read to
/// make; it never chooses what the read says.
/// </para>
/// <para>
/// Permission is decided here, once, before any read runs, and handed down as
/// plain booleans. The reading built from form answers needs
/// <c>applications.view_responses</c> and the shelf needs
/// <c>inventory.manage</c>, exactly as they do on their own screens -- and a
/// refusal names the permission rather than returning an empty result, because
/// "nobody said" and "you may not see this" are different answers.
/// </para>
/// <para>
/// Threads belong to one person and are never shared. An answer was computed
/// against the permissions its asker held at the time, and a stored sentence
/// cannot be re-checked against somebody else's -- so the only safe reader is
/// the person who asked.
/// </para>
/// </remarks>
public static class AssistantEndpoints
{
    /// <summary>
    /// Long enough for a real question, short enough that nobody pastes a
    /// document in. A page of text is not a question, and the cost of finding
    /// that out is paid per call.
    /// </summary>
    private const int MaxQuestion = 500;

    /// <summary>How many threads the list offers.</summary>
    private const int MaxThreads = 30;

    /// <summary>
    /// How long a conversation is kept.
    /// </summary>
    /// <remarks>
    /// Threads hold figures about applicants and the questions somebody
    /// thought to ask, which is worth keeping for a season and not worth
    /// keeping forever. Thirty days spans a review cycle: long enough to pick
    /// up Monday's thread on Friday, short enough that last year's questions
    /// are not still sitting there.
    /// </remarks>
    private static readonly TimeSpan Keep = TimeSpan.FromDays(30);

    public static IEndpointRouteBuilder MapAssistant(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/assistant/threads", Threads)
           .RequirePermission(Permission.ApplicationsView);

        app.MapGet("/admin/assistant/threads/{id:guid}", Thread)
           .RequirePermission(Permission.ApplicationsView);

        app.MapDelete("/admin/assistant/threads/{id:guid}", Forget)
           .RequirePermission(Permission.ApplicationsView);

        app.MapPost("/admin/assistant/chat", Chat)
           .RequirePermission(Permission.ApplicationsView);

        return app;
    }

    /// <remarks>
    /// Nullable for the reason every other request record here is: minimal
    /// APIs bind the body before the permission filter runs, so a required
    /// body would answer a request the gate has not looked at yet.
    /// </remarks>
    /// <param name="Screen">
    /// Which console screen the question was asked from, as one of the names
    /// <see cref="Screens"/> knows. Anything else is dropped rather than
    /// refused: a browser that sends a screen this version has not heard of
    /// should get its question answered, just without the hint.
    /// </param>
    public sealed record ChatRequest(
        string? Question, string? EventId, string? ThreadId, string? Screen);

    private static async Task<IResult> Threads(
        HttpContext http, IAssistantConversations conversations, CancellationToken ct)
    {
        // The sweep rides along on the list, which is the screen somebody
        // opens before they ask anything. Atlas has no scheduler, so retention
        // either happens off the back of ordinary use or it does not happen.
        await conversations.SweepAsync(DateTimeOffset.UtcNow - Keep, ct);

        var mine = await conversations.MineAsync(http.PersonId(), MaxThreads, ct);

        return Results.Ok(new
        {
            threads = mine.Select(t => new
            {
                id = t.Id,
                title = t.Title,
                eventId = t.EventId,
                lastAt = t.LastAt,
            }),
            keptForDays = (int)Keep.TotalDays,
        });
    }

    private static async Task<IResult> Thread(
        Guid id, HttpContext http, IAssistantConversations conversations, CancellationToken ct)
    {
        if (await conversations.ReadAsync(http.PersonId(), id, ct) is not { } thread)
        {
            return Results.NotFound();
        }

        return Results.Ok(Rendered(thread));
    }

    private static async Task<IResult> Forget(
        Guid id, HttpContext http, IAssistantConversations conversations, CancellationToken ct)
    {
        await conversations.ForgetAsync(http.PersonId(), id, ct);

        // 204 whether or not it was there. A thread that is not this person's
        // answers the same as one that is gone, so nobody learns from a delete
        // whether somebody else's thread exists.
        return Results.NoContent();
    }

    private static async Task<IResult> Chat(
        ChatRequest? body, HttpContext http, IChatAgent agent,
        IAssistantConversations conversations, IApplicantAnalyticsStore analytics,
        IInventoryStore inventory, IApplicantStore applicants,
        IEmailReachRead email, IFormsRead forms, IInventoryBurnRead burn,
        PermissionService permissions, CancellationToken ct)
    {
        var question = body?.Question?.Trim() ?? "";

        if (question.Length == 0)
        {
            // COPY: needs sign-off.
            return Results.BadRequest(new { error = "Ask something first." });
        }

        if (question.Length > MaxQuestion)
        {
            // COPY: needs sign-off.
            return Results.BadRequest(new
            {
                error = $"That is longer than a question. Keep it under {MaxQuestion} characters.",
            });
        }

        if (!Guid.TryParse(body?.EventId, out var eventId))
        {
            // Absent rather than guessed. Answering "how many confirmed"
            // against whichever event came first is a wrong answer that looks
            // right, which is the one kind of wrong this is built not to be.
            // COPY: needs sign-off.
            return Results.BadRequest(new { error = "Pick an event first." });
        }

        var person = http.PersonId();

        // The thread, or a new one titled after the question that opens it.
        // A thread id that is not this person's is treated as absent rather
        // than refused: the caller gets a new thread, and learns nothing about
        // whose the other one was.
        Guid threadId;
        IReadOnlyList<Turn> history = [];

        if (Guid.TryParse(body?.ThreadId, out var asked)
            && await conversations.ReadAsync(person, asked, ct) is { } existing)
        {
            threadId = existing.Id;
            history = existing.Turns;
        }
        else
        {
            threadId = await conversations.StartAsync(person, eventId, question, ct);
        }

        var mine = await permissions.ForAsync(person, ct);

        var context = new ToolContext(
            eventId,
            new Allowed(
                Responses: mine.Can(Permission.ApplicationsViewResponses),
                Inventory: mine.Can(Permission.InventoryManage),
                EmailStats: mine.Can(Permission.EmailViewStats)),
            analytics,
            inventory,
            DateOnly.FromDateTime(DateTime.UtcNow))
        {
            Applicants = applicants,
            Forms = forms,
            Burn = burn,
            EmailReach = email,

            // Matched against the closed list here rather than carried as
            // sent. What arrives goes into a prompt, and the browser is on the
            // other side of a boundary.
            Screen = Screens.Describe(body?.Screen) is null ? null : body?.Screen,
        };

        var reply = await agent.AnswerAsync(history, question, context, ct);

        // The question is recorded before the answer, so a thread reads in the
        // order it happened even when two questions race.
        await conversations.AppendAsync(
            person, threadId, new Turn(Speaker.Person, question, [], DateTimeOffset.UtcNow), ct);
        await conversations.AppendAsync(
            person, threadId,
            new Turn(Speaker.Assistant, reply.Body, reply.Basis, DateTimeOffset.UtcNow), ct);

        return Results.Ok(new
        {
            threadId,
            answer = reply.Body,
            basis = reply.Basis,
        });
    }

    private static object Rendered(ThreadDetail thread) => new
    {
        id = thread.Id,
        title = thread.Title,
        eventId = thread.EventId,
        turns = thread.Turns.Select(t => new
        {
            speaker = t.Speaker == Speaker.Person ? "person" : "assistant",
            body = t.Body,
            basis = t.Basis,
            at = t.At,
        }),
    };
}
