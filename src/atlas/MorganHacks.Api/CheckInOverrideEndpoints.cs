using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Identity.Domain;
using MorganHacks.Observability;

namespace MorganHacks.Api;

/// <summary>
/// Manual overrides at the check-in desk: admitting somebody without a code,
/// and correcting a name the desk cannot verify against.
/// </summary>
/// <remarks>
/// <para>
/// The room this exists in: somebody is standing at the desk with a broken QR
/// code and a queue behind them, or their name is spelled wrong on the
/// application and the portal has locked them out of fixing it. The
/// alternative to these routes is an organizer with database access typing an
/// UPDATE at the desk — which is the thing this removes, not a rule being
/// bypassed but a rule being bypassed invisibly.
/// </para>
/// <para>
/// Gated on <c>checkin.override</c>, deliberately not
/// <see cref="Permission.CheckinScan"/>: the volunteer scanning badges and
/// the person authorised to override are not always the same person, and one
/// string would make every volunteer the other one. A holder of
/// <c>checkin.scan</c> alone gets 403 here, exactly as an override-holder
/// without a scanner would get on <c>/admin/check-in/scan</c>.
/// </para>
/// <para>
/// What these routes refuse is as designed as what they do. There is no
/// status field on either request: the admit route moves an application to
/// <c>checked_in</c> or throws, judged by <see cref="StatusTransition"/> like
/// every other move in the system, and no request shape can express "mark
/// them accepted". A decision-shaped action offered at the desk is a decision
/// made at the desk, and the desk has neither the context nor the queue to
/// make it in. The refusal sentences — and they are still refusals, with the
/// same wording the scan gives — route onward rather than around.
/// </para>
/// <para>
/// Both writes record who did it and why: the reason is required here,
/// carried into <c>applications.status_history</c> / the override row, and
/// pushed into <c>audit.entries</c> by a trigger in the same transaction. An
/// override with no reason attached is indistinguishable from a mistake when
/// somebody reads it back in March, so the place to refuse one is before it
/// happens.
/// </para>
/// <para>
/// Nothing here logs a reason or a name. Both are sentences about a person;
/// the log line carries the actor and the application and is counted, while
/// the record that outlives the window carries the rest.
/// </para>
/// </remarks>
public static class CheckInOverrideEndpoints
{
    public static IEndpointRouteBuilder MapCheckInOverride(this IEndpointRouteBuilder app)
    {
        var overrides = app.MapGroup("/admin/check-in/override");

        overrides.MapGet("/find", Find)
                 .RequirePermission(Permission.CheckinOverride);
        overrides.MapPost("/admit", Admit)
                 .RequirePermission(Permission.CheckinOverride);
        overrides.MapPost("/correct", Correct)
                 .RequirePermission(Permission.CheckinOverride);

        return app;
    }

    /// <summary>
    /// As long as a reason may be. The same bound a decision reason gets —
    /// this is the same kind of sentence for the same kind of reader.
    /// </summary>
    private const int MaxReason = 500;

    /// <summary>What a name may hold, matching the applicant's own form.</summary>
    private const int NameLimit = 120;

    /// <summary>
    /// Finds an application by name or email, for the desk when the code will
    /// not scan. Requires <c>checkin.override</c>.
    /// </summary>
    /// <remarks>
    /// Answers with the same five fields the scan answers with — id, status,
    /// name, time if already in — because this record is what the desk looks
    /// at while looking at the person in front of them, and a school or an
    /// email address on it would be a screen held up in a public doorway.
    /// The email is matched but never returned.
    /// </remarks>
    private static async Task<IResult> Find(
        ICheckInStore codes,
        CancellationToken ct,
        string? q = null,
        Guid? eventId = null)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return Results.BadRequest(new { error = "Type a name or an email." });
        }

        if (query.Length > 200)
        {
            // Bounded because this text goes straight into a pattern match.
            // Generous enough that a paste of a whole address block still
            // gets refused rather than quietly matching ten rows of nothing.
            return Results.BadRequest(new { error = "That is longer than a name." });
        }

        var matches = await codes.SearchAsync(query, eventId, ct);

        return Results.Ok(new
        {
            matches = matches.Select(subject => new
            {
                applicationId = subject.ApplicationId,
                status = subject.Status.ToWire(),
                name = Name(subject),
                checkedInAt = subject.CheckedInAt,
                eventId = subject.EventId,
                eventName = subject.EventName,
                // The event they applied to, as readable text. Returned under
                // two common names so a search result is never ambiguous
                // about which event a match belongs to — an override acts on
                // exactly one application, and a name that recurs across
                // events is not a person the desk can safely guess at.
                @event = subject.EventName,
            }),
        });
    }

    /// <summary>
    /// The body of an admit: which application, and why. Requires
    /// <c>checkin.override</c>.
    /// </summary>
    /// <remarks>
    /// Nullable members for the binder reason every body here has: minimal
    /// APIs bind before endpoint filters run, so a required body would answer
    /// a request with no session by describing the route instead of refusing
    /// it.
    /// </remarks>
    public sealed record AdmitRequest(Guid? ApplicationId, string? Reason);

    /// <summary>
    /// Checks somebody in without a code. Requires <c>checkin.override</c>.
    /// </summary>
    /// <remarks>
    /// The same four answers the scan gives, in the same words, because the
    /// person reading them is at the same desk with the same queue — only the
    /// route to the answer changed. Not confirmed is still not confirmed: the
    /// lifecycle is the lifecycle, and the sentence telling an organizer to
    /// confirm a spot is still true. What this route removes is the code as
    /// the price of asking.
    /// </remarks>
    private static async Task<IResult> Admit(
        AdmitRequest? request,
        HttpContext http,
        ICheckInStore codes,
        IApplicationStore applications,
        ILogger<AdmitRequest> log,
        CancellationToken ct)
    {
        if (request?.ApplicationId is null)
        {
            return Results.BadRequest(new { error = "Find the person first." });
        }

        var id = request.ApplicationId.Value;
        var reason = Trimmed(request.Reason);
        if (string.IsNullOrEmpty(reason))
        {
            return Results.BadRequest(new
            {
                error = "An override needs a reason. Say what happened.",
            });
        }

        if (reason.Length > MaxReason)
        {
            return Results.BadRequest(new
            {
                error = $"A reason has to be {MaxReason} characters or fewer. "
                        + "Anything longer belongs in a note.",
            });
        }

        var subject = await codes.FindAsync(id, ct);
        if (subject is null)
        {
            return NotFound(http, log, id);
        }

        if (subject.Status is ApplicationStatus.CheckedIn)
        {
            // Already in. Still 200, still nothing written — the same
            // decision the second scan makes, and for the same reason: the
            // desk's question is whether this person may come in, and it is.
            return Admitted(http, log, ScanOutcome.AlreadyCheckedIn, subject, subject.CheckedInAt);
        }

        if (!StatusTransition.IsAllowed(subject.Status, ApplicationStatus.CheckedIn))
        {
            log.LogInformation(
                "An override was refused. {actor} {applicationId} {outcome} {event}",
                http.PersonId(), id, ScanOutcome.NotConfirmed, Events.CheckInOverridden);

            return Results.Conflict(new
            {
                outcome = ScanOutcome.NotConfirmed,
                error = CheckInDesk.WhyNot(subject.Status),
                status = subject.Status.ToWire(),
                name = Name(subject),
            });
        }

        try
        {
            var change = await applications.OverrideCheckInAsync(
                id, actorId: http.PersonId(), reason: reason, ct: ct);

            return Admitted(http, log, ScanOutcome.CheckedIn, subject, change.At);
        }
        catch (InvalidTransitionException e) when (e.From is ApplicationStatus.CheckedIn)
        {
            // Somebody scanned them between the read above and this write.
            // The answer is the one the scan already got, read fresh so the
            // time on screen is when they were let in.
            var settled = await codes.FindAsync(id, ct);
            return Admitted(
                http, log, ScanOutcome.AlreadyCheckedIn, subject, settled?.CheckedInAt);
        }
        catch (InvalidTransitionException e)
        {
            // They moved between the read and the lock — to a status this
            // still refuses. Same sentence the pre-check gives, against what
            // is now true rather than what was.
            log.LogInformation(
                "An override was refused. {actor} {applicationId} {outcome} {event}",
                http.PersonId(), id, ScanOutcome.NotConfirmed, Events.CheckInOverridden);

            return Results.Conflict(new
            {
                outcome = ScanOutcome.NotConfirmed,
                error = CheckInDesk.WhyNot(e.From),
                status = e.From.ToWire(),
                name = Name(subject),
            });
        }
        catch (InvalidOperationException)
        {
            // The store's answer for an id that names no application, after
            // the transition cases above.
            return NotFound(http, log, id);
        }
    }

    /// <summary>
    /// The body of a correction: the name as it should read, and why.
    /// </summary>
    public sealed record CorrectRequest(
        Guid? ApplicationId,
        string? FirstName,
        string? LastName,
        string? Reason);

    /// <summary>
    /// Rewrites the name on an application. Requires
    /// <c>checkin.override</c>.
    /// </summary>
    /// <remarks>
    /// The desk's half of the sentence the portal gives when
    /// <c>ProfileEditing</c> has locked the form: "Email us and we will change
    /// them for you." On the day they are standing in front of you, so this
    /// is where the email would have landed — with a reason, into a trail,
    /// and touching nothing but the two names.
    /// </remarks>
    private static async Task<IResult> Correct(
        CorrectRequest? request,
        HttpContext http,
        IApplicationStore applications,
        ILogger<CorrectRequest> log,
        CancellationToken ct)
    {
        if (request?.ApplicationId is null)
        {
            return Results.BadRequest(new { error = "Find the person first." });
        }

        var id = request.ApplicationId.Value;

        // Both halves, always. They are NOT NULL from the moment an
        // application stops being a draft, so a half-empty name here would be
        // a write the completeness constraint could not accept — the desk gets
        // the same rule the applicant's own form gives, before the database
        // has to.
        var firstName = Trimmed(request.FirstName);
        if (string.IsNullOrEmpty(firstName))
        {
            return Results.BadRequest(new { error = "Their first name is required." });
        }

        var lastName = Trimmed(request.LastName);
        if (string.IsNullOrEmpty(lastName))
        {
            return Results.BadRequest(new { error = "Their last name is required." });
        }

        if (firstName.Length > NameLimit || lastName.Length > NameLimit)
        {
            return Results.BadRequest(new { error = "That answer is longer than we can store." });
        }

        var reason = Trimmed(request.Reason);
        if (string.IsNullOrEmpty(reason))
        {
            return Results.BadRequest(new
            {
                error = "An override needs a reason. Say what happened.",
            });
        }

        if (reason.Length > MaxReason)
        {
            return Results.BadRequest(new
            {
                error = $"A reason has to be {MaxReason} characters or fewer. "
                        + "Anything longer belongs in a note.",
            });
        }

        bool applied;
        try
        {
            applied = await applications.CorrectNameAsync(
                id, firstName, lastName, actorId: http.PersonId(), reason: reason, ct: ct);
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { error = "No such applicant." });
        }

        if (!applied)
        {
            // Nothing written, so no override row and no trail entry: a
            // correction that changed nothing must not read as one that did.
            return Results.Conflict(new
            {
                error = "Nothing to correct. Their name is already that.",
            });
        }

        log.LogInformation(
            "An application's name was corrected at the desk. {actor} {applicationId} {event}",
            http.PersonId(), id, Events.ApplicationCorrected);

        return Results.Ok(new
        {
            corrected = true,
            name = $"{firstName} {lastName}".Trim(),
        });
    }

    private static IResult NotFound(HttpContext http, ILogger log, Guid applicationId)
    {
        // The id named nothing — it was never real, or it was deleted between
        // two reads. Either way the desk gets a sentence rather than a stack
        // trace, and the line goes out because a burst of these on the morning
        // is worth seeing.
        log.LogInformation(
            "An override matched no application. {actor} {applicationId} {event}",
            http.PersonId(), applicationId, Events.CheckInOverridden);

        return Results.NotFound(new { error = "No such applicant." });
    }

    private static IResult Admitted(
        HttpContext http,
        ILogger log,
        ScanOutcome outcome,
        CheckInSubject subject,
        DateTimeOffset? at)
    {
        log.LogInformation(
            "An override admitted somebody without a code. {actor} {applicationId} {outcome} {event}",
            http.PersonId(), subject.ApplicationId, outcome, Events.CheckInOverridden);

        return Results.Ok(new
        {
            outcome,
            message = CheckInDesk.Describe(outcome),
            name = Name(subject),
            alreadyCheckedIn = outcome is ScanOutcome.AlreadyCheckedIn,
            checkedInAt = at,
        });
    }

    /// <summary>The trimmed body field, or null when there wasn't one.</summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The name to show, or null when the row somehow has none.
    /// </summary>
    private static string? Name(CheckInSubject subject)
    {
        var name = $"{subject.FirstName} {subject.LastName}".Trim();
        return name.Length == 0 ? null : name;
    }
}
