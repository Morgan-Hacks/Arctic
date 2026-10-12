namespace MorganHacks.Applications.Assistant;

/// <summary>How a campaign's mail performed, in the round.</summary>
public sealed record EmailReach(
    long Sent,
    long Tracked,
    long Clicked,
    long TotalClicks,
    long OpenTracked,
    long Opened,
    long AutomatedEvents);

/// <summary>
/// Email figures, as the assistant needs them.
/// </summary>
/// <remarks>
/// A port rather than a project reference. The store these come from lives in
/// lark, which this assembly deliberately does not reference -- it references
/// no other project at all, which is what keeps the tool layer from quietly
/// growing reach. The API implements this over lark's own store and hands it
/// down, so the dependency points the right way.
/// </remarks>
public interface IEmailReachRead
{
    Task<EmailReach?> ReadAsync(CancellationToken ct = default);
}

/// <summary>One question on a form, and what people did with it.</summary>
/// <param name="Skipped">
/// How many submitted responses left it blank. The figure worth having: a
/// question people decline to answer is a question to cut, and a distribution
/// of only the answers given hides exactly that.
/// </param>
public sealed record QuestionUptake(
    string Question,
    int Answered,
    int Skipped,
    IReadOnlyList<AnalysisRow> Answers);

/// <summary>One of the event's forms, as the list screen shows it.</summary>
/// <param name="Name">
/// Unique within the event, which the stored name is not. Two forms may be
/// called the same thing, and a name the model picks a form by has to pick
/// exactly one -- so a repeated name carries its code.
/// </param>
/// <param name="Status">
/// <c>live</c>, <c>draft</c> or <c>closed</c>. A form with no published
/// version has never been seen by anybody, and reporting its nil response
/// count beside a live form's would read as a form nobody filled in rather
/// than one nobody was shown.
/// </param>
public sealed record FormSummary(
    string Name,
    string Kind,
    string Status,
    int Questions,
    long Responses,
    DateTimeOffset? ClosesAt);

/// <summary>
/// The event's forms, and what people did with them.
/// </summary>
/// <remarks>
/// <para>
/// A port rather than a project reference, for the reason the others are:
/// this assembly references no other project, and the stores these read live
/// in the layer above.
/// </para>
/// <para>
/// Forms are listed as well as counted because an assistant that can read a
/// form's answers but cannot see that the form exists answers a question
/// about forms out of the only tool it has, and calls a question a form. That
/// was not a prompt problem. It was a missing read.
/// </para>
/// </remarks>
public interface IFormsRead
{
    /// <summary>
    /// The names, for the closed list the model picks a form from.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ListAsync"/> and deliberately cheaper: this
    /// one runs on every question, to build the tool schema, and counting
    /// responses per form to do it would put a query per form behind "how
    /// many shirts are left".
    /// </remarks>
    Task<IReadOnlyList<string>> NamesAsync(Guid eventId, CancellationToken ct = default);

    Task<IReadOnlyList<FormSummary>> ListAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Per-question answers, across every version of one form.
    /// </summary>
    /// <remarks>
    /// Every version rather than the published one, for the reason the
    /// applicant detail screen reads them all: a question rebuilt under a new
    /// key would otherwise file half the cohort's answers under a heading they
    /// never saw.
    /// </remarks>
    /// <param name="form">
    /// The name <see cref="NamesAsync"/> gave it. Null means the application
    /// form, which is what a question about "the form" nearly always means.
    /// </param>
    /// <returns>Null when the event has no such form.</returns>
    Task<IReadOnlyList<QuestionUptake>?> UptakeAsync(
        Guid eventId, string? form, CancellationToken ct = default);
}

/// <summary>What has left the shelf, and how fast.</summary>
/// <param name="PerDay">
/// Issued per day since the first movement. Null when nothing has moved, or
/// when everything moved today -- a rate measured over no elapsed time is a
/// division by zero dressed up as a forecast.
/// </param>
public sealed record Burn(
    string Item,
    int Left,
    int Issued,
    int Restocked,
    double? PerDay,
    int? DaysLeft);

/// <summary>The shelf's ledger, turned into rates.</summary>
public interface IInventoryBurnRead
{
    Task<IReadOnlyList<Burn>> ReadAsync(Guid eventId, CancellationToken ct = default);
}
