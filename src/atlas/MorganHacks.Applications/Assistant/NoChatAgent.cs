namespace MorganHacks.Applications.Assistant;

/// <summary>
/// The assistant when no model is configured.
/// </summary>
/// <remarks>
/// <para>
/// It says so, and it says nothing else. There is deliberately no fallback
/// that answers from a word table: the single-shot filter box had one because
/// a filter box must always do something, and that is precisely what let a
/// dead key go unnoticed for an afternoon -- a quieter answer is
/// indistinguishable from a good one, and nobody goes looking.
/// </para>
/// <para>
/// Registered rather than leaving the service missing, so a console with no
/// key gets a sentence instead of a 500. The screens it would have read from
/// are all still there and still correct, which the sentence says.
/// </para>
/// </remarks>
public sealed class NoChatAgent : IChatAgent
{
    public Task<ChatReply> AnswerAsync(
        IReadOnlyList<Turn> history, string question, ToolContext context, CancellationToken ct)
        // COPY: needs sign-off.
        => Task.FromResult(new ChatReply(
            "There is no assistant configured on this deployment, so I cannot "
            + "answer questions here. Every figure it would have quoted is on "
            + "the screens themselves.",
            []));
}
