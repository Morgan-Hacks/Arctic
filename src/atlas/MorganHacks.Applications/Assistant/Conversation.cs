namespace MorganHacks.Applications.Assistant;

/// <summary>Who said a thing.</summary>
public enum Speaker
{
    Person,
    Assistant,
}

/// <summary>One thing said, by one side.</summary>
/// <param name="Basis">
/// The reads an assistant turn was built from, by name. Empty for a turn that
/// needed none -- a refusal, or an answer to "what can you tell me" -- and
/// always empty for a person's turn, because a question is not computed.
/// </param>
public sealed record Turn(
    Speaker Speaker,
    string Body,
    IReadOnlyList<string> Basis,
    DateTimeOffset At);

/// <summary>A conversation, as the list screen shows it.</summary>
public sealed record ThreadSummary(
    Guid Id,
    string Title,
    Guid? EventId,
    DateTimeOffset LastAt);

/// <summary>A conversation and everything said in it.</summary>
public sealed record ThreadDetail(
    Guid Id,
    string Title,
    Guid? EventId,
    IReadOnlyList<Turn> Turns);

/// <summary>
/// Where conversations live.
/// </summary>
/// <remarks>
/// Every method takes the asking person and filters on them. Not as a
/// courtesy: a stored answer was computed against the permissions its asker
/// held, and there is no way to re-check a sentence against somebody else's.
/// Ownership is the only check available, so it is the one the store makes,
/// and it makes it rather than trusting a caller to pass the right id.
/// </remarks>
public interface IAssistantConversations
{
    Task<IReadOnlyList<ThreadSummary>> MineAsync(
        Guid personId, int limit, CancellationToken ct = default);

    /// <summary>The thread, or null when it is not this person's.</summary>
    Task<ThreadDetail?> ReadAsync(
        Guid personId, Guid threadId, CancellationToken ct = default);

    /// <summary>Starts a thread, titled after the question that opened it.</summary>
    Task<Guid> StartAsync(
        Guid personId, Guid? eventId, string title, CancellationToken ct = default);

    /// <summary>Appends a turn, and touches the thread's place in the list.</summary>
    Task AppendAsync(
        Guid personId, Guid threadId, Turn turn, CancellationToken ct = default);

    /// <summary>Forgets a thread. Silent when it was not this person's.</summary>
    Task ForgetAsync(Guid personId, Guid threadId, CancellationToken ct = default);

    /// <summary>
    /// Drops every thread last touched before <paramref name="before"/>,
    /// whoever it belonged to, and answers how many went.
    /// </summary>
    Task<int> SweepAsync(DateTimeOffset before, CancellationToken ct = default);
}
