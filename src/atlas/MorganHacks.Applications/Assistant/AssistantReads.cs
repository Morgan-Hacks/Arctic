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

/// <summary>
/// Per-question answers, across every version of the application form.
/// </summary>
/// <remarks>
/// Every version rather than the published one, for the reason the applicant
/// detail screen reads them all: a question rebuilt under a new key would
/// otherwise file half the cohort's answers under a heading they never saw.
/// </remarks>
public interface IFormUptakeRead
{
    Task<IReadOnlyList<QuestionUptake>?> ReadAsync(
        Guid eventId, CancellationToken ct = default);
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
