using MorganHacks.Applications.Assistant;
using MorganHacks.Applications.Domain;
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
    public sealed record AskRequest(string? Question);

    private static async Task<IResult> Ask(
        AskRequest? body, IQuestionRouter router, CancellationToken ct)
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
            plan = new
            {
                view = plan.View switch
                {
                    AssistantView.Applicants => "applicants",
                    AssistantView.Count => "count",
                    _ => "dashboard",
                },
                statuses = plan.Statuses.Select(s => s.ToWire()).ToArray(),
                countOf = plan.CountOf?.ToWire(),
                search = plan.Search,
                explain = plan.Explain,
            },
        });
    }
}
