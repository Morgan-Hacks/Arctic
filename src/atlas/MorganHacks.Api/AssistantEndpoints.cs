using MorganHacks.Applications.Assistant;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Identity;
using MorganHacks.Identity.Domain;

namespace MorganHacks.Api;

/// <summary>
/// Asking the console a question in words.
/// </summary>
/// <remarks>
/// <para>
/// One route, and it answers with a <see cref="AssistantPlan"/> -- a screen
/// and the filters to put on it. It never answers with an applicant.
/// </para>
/// <para>
/// That is not a limitation worked around, it is the design. The console takes
/// the plan and fetches the named screen through the endpoint it already uses,
/// as the person asking, and that endpoint applies the permission it always
/// applied. Nothing here reads an application, so nothing here can leak one --
/// and a reader that is confused, or talked into something by text it was
/// shown, can at worst name a screen somebody may not open, which refuses on
/// its own.
/// </para>
/// <para>
/// <c>applications.view</c> on the route anyway. Not because a plan is
/// sensitive -- it is three words and a status -- but because somebody who
/// cannot open any of the screens this names has no business spending the
/// question on, and the refusal they get here is the one they would get there.
/// </para>
/// </remarks>
public static class AssistantEndpoints
{
    /// <summary>
    /// Long enough for a real question, short enough that nobody pastes a
    /// document into it. The reader downstream is told to answer questions;
    /// a page of text is not one, and the cost of finding that out is paid
    /// per call.
    /// </summary>
    private const int MaxQuestion = 300;

    public static IEndpointRouteBuilder MapAssistant(this IEndpointRouteBuilder app)
    {
        app.MapPost("/admin/assistant/ask", Ask)
           .RequirePermission(Permission.ApplicationsView);

        return app;
    }

    /// <remarks>
    /// Nullable for the reason every other request record here is: minimal
    /// APIs bind the body before the permission filter runs, so a required
    /// body would answer a request the gate has not looked at yet.
    /// </remarks>
    public sealed record AskRequest(string? Question, string? EventId);


    /// <summary>
    /// The figure a counting question asked for, or nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The number is read out of the same store the applicants screen reads it
    /// out of. The model chose which figure; it never produced one, and there
    /// is no path here by which it could -- which is what keeps "the assistant
    /// cannot be confidently wrong about a number" true now that it answers
    /// with numbers at all.
    /// </para>
    /// <para>
    /// This is the line the feature holds as it grows: the assistant may hand
    /// back what the screen it named would have shown this caller, and never
    /// more. A count is already on that screen, on the filter chips. The route
    /// is behind <c>applications.view</c>, which is the permission that screen
    /// needs, so nobody learns anything here they could not have read by
    /// clicking.
    /// </para>
    /// <para>
    /// Absent rather than zero when the event is unknown or unreadable. Zero
    /// is a real answer -- "none of them have checked in" -- and inventing it
    /// out of a missing parameter would be the one kind of wrong this is built
    /// not to be.
    /// </para>
    /// </remarks>
    private static async Task<object?> AnswerAsync(
        AssistantPlan plan, string? eventId, IApplicantStore applicants,
        PostgresApplicantAnalyticsStore analytics, CancellationToken ct)
    {
        if (!Guid.TryParse(eventId, out var id))
        {
            return null;
        }

        if (plan.Metric is { } metric)
        {
            // The same read the board is drawn from. `includeResponses: false`
            // because none of these readings touch an answer somebody wrote --
            // they are proportions of counts, and asking for the responses
            // would be asking for more than the question needs.
            var data = await analytics.ReadAsync(
                id, includeResponses: false, DateOnly.FromDateTime(DateTime.UtcNow), ct);

            return Analyse.Of(metric, data) is not { } reading
                ? null
                : new
                {
                    headline = reading.Headline,
                    basis = reading.Basis,
                    rows = reading.Rows.Select(r => new { label = r.Label, count = r.Count, share = r.Share }),
                };
        }

        if (plan.CountOf is not { } counted)
        {
            return null;
        }

        var counts = await applicants.CountsAsync(id, ct);

        // No key for a status nothing is sitting in, which is the store being
        // careful rather than an absence of data -- so zero is right here.
        return new { value = counts.TryGetValue(counted, out var n) ? n : 0 };
    }

    private static async Task<IResult> Ask(
        AskRequest? body, IQuestionRouter router, IApplicantStore applicants,
        PostgresApplicantAnalyticsStore analytics, CancellationToken ct)
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

        var answer = await router.PlanAsync(question, ct);

        if (answer.Plan is not { } plan)
        {
            // 200, not 400. The caller asked a well-formed question that this
            // cannot place, which is an answer about the question rather than
            // a fault in the request -- and the screen has a sentence to show
            // either way.
            return Results.Ok(new { refusal = answer.Refusal });
        }

        return Results.Ok(new
        {
            answer = await AnswerAsync(plan, body?.EventId, applicants, analytics, ct),
            plan = new
            {
                view = plan.View switch
                {
                    AssistantView.Count => "count",
                    AssistantView.Analysis => "analysis",
                    _ => "applicants",
                },
                statuses = plan.Statuses.Select(s => s.ToWire()).ToArray(),
                countOf = plan.CountOf?.ToWire(),
                search = plan.Search,
                explain = plan.Explain,
            },
        });
    }
}
