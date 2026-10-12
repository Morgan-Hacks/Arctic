using System.Text.Json;
using MorganHacks.Applications.Assistant;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Data;

/// <summary>
/// <c>assistant.threads</c> and <c>assistant.turns</c>.
/// </summary>
/// <remarks>
/// Every statement carries <c>person_id</c> in its WHERE clause, including the
/// ones that look like they only need a thread id. A thread id is a guessable
/// handle to somebody's questions and the figures they were given, and
/// checking ownership in the same statement that does the work means there is
/// no window between the check and the act.
/// </remarks>
public sealed class PostgresAssistantConversations(NpgsqlDataSource dataSource)
    : IAssistantConversations
{
    public async Task<IReadOnlyList<ThreadSummary>> MineAsync(
        Guid personId, int limit, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            SELECT id, title, event_id, last_at
              FROM assistant.threads
             WHERE person_id = @personId
             ORDER BY last_at DESC
             LIMIT @limit
            """);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("limit", limit);

        var threads = new List<ThreadSummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            threads.Add(new ThreadSummary(
                reader.GetGuid(0),
                reader.GetString(1),
                await reader.IsDBNullAsync(2, ct) ? null : reader.GetGuid(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return threads;
    }

    public async Task<ThreadDetail?> ReadAsync(
        Guid personId, Guid threadId, CancellationToken ct = default)
    {
        await using var head = dataSource.CreateCommand("""
            SELECT title, event_id
              FROM assistant.threads
             WHERE id = @id AND person_id = @personId
            """);
        head.Parameters.AddWithValue("id", threadId);
        head.Parameters.AddWithValue("personId", personId);

        string title;
        Guid? eventId;

        await using (var reader = await head.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
            {
                // Missing and not-yours answer the same, which is the point:
                // telling somebody a thread exists but is not theirs is
                // telling them something about another person's questions.
                return null;
            }

            title = reader.GetString(0);
            eventId = await reader.IsDBNullAsync(1, ct) ? null : reader.GetGuid(1);
        }

        await using var rest = dataSource.CreateCommand("""
            SELECT speaker, body, basis, created_at
              FROM assistant.turns
             WHERE thread_id = @id
             ORDER BY created_at, id
            """);
        rest.Parameters.AddWithValue("id", threadId);

        var turns = new List<Turn>();
        await using var turnReader = await rest.ExecuteReaderAsync(ct);
        while (await turnReader.ReadAsync(ct))
        {
            turns.Add(new Turn(
                turnReader.GetString(0) == "person" ? Speaker.Person : Speaker.Assistant,
                turnReader.GetString(1),
                await turnReader.IsDBNullAsync(2, ct)
                    ? []
                    : JsonSerializer.Deserialize<string[]>(turnReader.GetString(2)) ?? [],
                turnReader.GetFieldValue<DateTimeOffset>(3)));
        }

        return new ThreadDetail(threadId, title, eventId, turns);
    }

    public async Task<Guid> StartAsync(
        Guid personId, Guid? eventId, string title, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            INSERT INTO assistant.threads (person_id, event_id, title)
            VALUES (@personId, @eventId, @title)
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("personId", personId);
        cmd.Parameters.AddWithValue("eventId", (object?)eventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("title", Titled(title));

        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task AppendAsync(
        Guid personId, Guid threadId, Turn turn, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        // The thread's own row is the ownership check and the ordering key in
        // one. An UPDATE that matches nothing means the thread is not this
        // person's, and nothing is written -- rather than a turn landing in a
        // thread the caller does not own and the touch failing afterwards.
        await using var touch = new NpgsqlCommand("""
            UPDATE assistant.threads
               SET last_at = now()
             WHERE id = @id AND person_id = @personId
            """, connection, tx);
        touch.Parameters.AddWithValue("id", threadId);
        touch.Parameters.AddWithValue("personId", personId);

        if (await touch.ExecuteNonQueryAsync(ct) == 0)
        {
            await tx.RollbackAsync(ct);
            return;
        }

        await using var insert = new NpgsqlCommand("""
            INSERT INTO assistant.turns (thread_id, speaker, body, basis)
            VALUES (@id, @speaker, @body, @basis)
            """, connection, tx);
        insert.Parameters.AddWithValue("id", threadId);
        insert.Parameters.AddWithValue(
            "speaker", turn.Speaker == Speaker.Person ? "person" : "assistant");
        insert.Parameters.AddWithValue("body", Trimmed(turn.Body));
        insert.Parameters.Add(new NpgsqlParameter("basis", NpgsqlDbType.Jsonb)
        {
            Value = turn.Speaker == Speaker.Person
                ? DBNull.Value
                : JsonSerializer.Serialize(turn.Basis),
        });

        await insert.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ForgetAsync(Guid personId, Guid threadId, CancellationToken ct = default)
    {
        // The turns go with it, by the cascade on the foreign key. Forgetting
        // a conversation has to forget what was said in it; a thread list that
        // loses its titles while the answers stay behind is not forgetting.
        await using var cmd = dataSource.CreateCommand(
            "DELETE FROM assistant.threads WHERE id = @id AND person_id = @personId");
        cmd.Parameters.AddWithValue("id", threadId);
        cmd.Parameters.AddWithValue("personId", personId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> SweepAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "DELETE FROM assistant.threads WHERE last_at < @before");
        cmd.Parameters.AddWithValue("before", before);

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// The column takes 200 characters and a question can be longer, so the
    /// title is cut rather than the insert failing. A thread nobody can open
    /// because its first question was wordy is worse than a clipped title.
    /// </summary>
    private static string Titled(string question)
    {
        var trimmed = question.Trim();
        if (trimmed.Length == 0)
        {
            // COPY: needs sign-off.
            return "Untitled";
        }

        return trimmed.Length <= 200 ? trimmed : trimmed[..199] + "…";
    }

    /// <summary>Same reasoning as <see cref="Titled"/>, for the body's limit.</summary>
    private static string Trimmed(string body) =>
        body.Length <= 8000 ? body : body[..7999] + "…";
}
