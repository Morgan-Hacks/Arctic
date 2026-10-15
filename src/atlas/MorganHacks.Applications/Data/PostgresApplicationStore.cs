using System.Text.Json;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

/// <summary>
/// The <c>applications.*</c> schema. Nothing outside this module touches it.
/// </summary>
public sealed class PostgresApplicationStore(NpgsqlDataSource dataSource) : IApplicationStore
{
    /// <summary>
    /// Creates the row the moment someone starts the form, rather than when
    /// they submit.
    /// </summary>
    /// <remarks>
    /// The first history row is written by a trigger, not here. An application
    /// whose trail begins at its second status is one whose trail cannot be
    /// trusted, and that has to hold for rows this method did not create.
    /// </remarks>
    public async Task<Guid> StartAsync(
        Guid eventId, string email, Guid? personId = null, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO applications.applications (event_id, person_id, email)
            VALUES (@eventId, @personId, @email)
            RETURNING id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("personId", (object?)personId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("email", email);
        return (Guid)(await cmd.ExecuteScalarAsync(ct))!;
    }

    public async Task<ApplicationStatus?> StatusOfAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT status FROM applications.applications WHERE id = @id");
        cmd.Parameters.AddWithValue("id", applicationId);
        return await cmd.ExecuteScalarAsync(ct) is string wire
            ? ApplicationStatuses.Parse(wire)
            : null;
    }

    /// <inheritdoc />
    public async Task<StatusChange> TransitionAsync(
        Guid applicationId,
        ApplicationStatus next,
        Guid? actorId = null,
        string? reason = null,
        Guid? batchId = null,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        var change = await TransitionAsync(
            connection, transaction, applicationId, next, actorId, reason, batchId, ct);

        // The trigger's insert is part of this transaction, so the status and
        // its history row commit together or not at all. That was already true
        // when this method wrote both; it is now true for every writer.
        await transaction.CommitAsync(ct);

        return change;
    }

    /// <summary>
    /// The status change itself, on a transaction somebody else owns.
    /// </summary>
    /// <remarks>
    /// Everything except opening and committing, so a caller that needs the
    /// status and something else to land together can hold one transaction
    /// around both without becoming a second implementation of the rules. The
    /// validation, the row lock and the context the triggers read are all in
    /// here exactly once — which is what "there is one writer for statuses"
    /// means in practice.
    /// </remarks>
    private static async Task<StatusChange> TransitionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid applicationId,
        ApplicationStatus next,
        Guid? actorId,
        string? reason,
        Guid? batchId,
        CancellationToken ct)
    {
        // FOR UPDATE, so the status is read and replaced under one row lock.
        //
        // Two reviewers reaching the same application at the same moment is
        // ordinary, not exotic — that is what a shared queue is. Without the
        // lock both read 'under_review', both find their change legal, and the
        // application ends up accepted with a history row saying it was
        // rejected. With it the second waits, re-reads 'accepted', and its
        // transition is judged against what is actually true.
        ApplicationStatus current;
        const string read = """
            SELECT status FROM applications.applications WHERE id = @id FOR UPDATE
            """;
        await using (var cmd = new NpgsqlCommand(read, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            if (await cmd.ExecuteScalarAsync(ct) is not string wire)
            {
                throw new InvalidOperationException($"No application {applicationId}.");
            }

            current = ApplicationStatuses.Parse(wire);
        }

        StatusTransition.Validate(current, next);

        // Who did this, and why, told to the transaction rather than passed to
        // an INSERT. The trigger writes the history row for every status
        // change including ones that never come through here, so this is how
        // it learns the things only the application knows.
        //
        // Transaction-local, so it cannot leak onto the next request that
        // borrows this pooled connection.
        const string context = """
            SELECT set_config('app.actor_id', @actorId, true),
                   set_config('app.reason', @reason, true),
                   set_config('app.batch_id', @batchId, true)
            """;
        await using (var cmd = new NpgsqlCommand(context, connection, transaction))
        {
            cmd.Parameters.AddWithValue("actorId", actorId?.ToString() ?? string.Empty);
            cmd.Parameters.AddWithValue("reason", reason ?? string.Empty);
            cmd.Parameters.AddWithValue("batchId", batchId?.ToString() ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Just the status. The lifecycle timestamps and the history row are
        // both the database's job now, which is what makes them hold for a
        // hand-written UPDATE during an incident as well as for this method.
        const string update = """
            UPDATE applications.applications
               SET status = @next
             WHERE id = @id
            RETURNING updated_at
            """;

        DateTimeOffset at;
        await using (var cmd = new NpgsqlCommand(update, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            cmd.Parameters.AddWithValue("next", next.ToWire());
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);

            // now() is transaction start time, so this is the same instant the
            // trigger stamped on the history row.
            at = reader.GetFieldValue<DateTimeOffset>(0);
        }

        return new StatusChange(applicationId, current, next, actorId, reason, batchId, at);
    }

    /// <inheritdoc />
    public async Task<StatusChange> OverrideCheckInAsync(
        Guid applicationId,
        Guid? actorId = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Same call a scan makes, judged against the same table. An
        // application that is not confirmed throws here before anything is
        // written, which is how "the desk cannot mark an application accepted"
        // is a fact about the code rather than a check somebody could forget.
        var change = await TransitionAsync(
            connection, transaction, applicationId, ApplicationStatus.CheckedIn,
            actorId, reason, batchId: null, ct);

        // The override row, in the transaction the transition just set the
        // actor context on, so its trigger writes the audit entry with the
        // same actor and the same transaction start time as the history row.
        const string record = """
            INSERT INTO applications.overrides (application_id, kind, reason)
            VALUES (@id, 'check_in', @reason)
            """;
        await using (var cmd = new NpgsqlCommand(record, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            cmd.Parameters.AddWithValue("reason", reason ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);

        return change;
    }

    /// <inheritdoc />
    public async Task<bool> CorrectNameAsync(
        Guid applicationId,
        string firstName,
        string lastName,
        Guid? actorId = null,
        string? reason = null,
        CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // FOR UPDATE, so the before-values in the override row are the ones
        // this write actually replaced. Two desks correcting the same person
        // take turns on the lock; the second one re-reads and either sees the
        // new name — and reports "nothing to correct" honestly — or writes a
        // second override whose 'from' is the first override's 'to', which is
        // a true sentence about what happened.
        string? storedFirst;
        string? storedLast;
        const string read = """
            SELECT first_name, last_name
              FROM applications.applications
             WHERE id = @id
             FOR UPDATE
            """;
        await using (var cmd = new NpgsqlCommand(read, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new InvalidOperationException($"No application {applicationId}.");
            }

            storedFirst = reader.IsDBNull(0) ? null : reader.GetString(0);
            storedLast = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        if (storedFirst == firstName && storedLast == lastName)
        {
            // Nothing written, so nothing to record. The transaction rolls
            // back on disposal; an override row for a no-op correction would
            // read as a change in March that never happened.
            return false;
        }

        // The context the triggers read. Transaction-local, like the
        // transition's: the override's audit row and its actor column both
        // come from here.
        const string context = """
            SELECT set_config('app.actor_id', @actorId, true),
                   set_config('app.reason', @reason, true),
                   set_config('app.batch_id', '', true)
            """;
        await using (var cmd = new NpgsqlCommand(context, connection, transaction))
        {
            cmd.Parameters.AddWithValue("actorId", actorId?.ToString() ?? string.Empty);
            cmd.Parameters.AddWithValue("reason", reason ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // Only what actually differs goes into `changes`, so a correction of
        // one name does not claim the other one was rewritten too.
        var changes = new Dictionary<string, Dictionary<string, string?>>();
        if (storedFirst != firstName)
        {
            changes["firstName"] = new() { ["from"] = storedFirst, ["to"] = firstName };
        }

        if (storedLast != lastName)
        {
            changes["lastName"] = new() { ["from"] = storedLast, ["to"] = lastName };
        }

        const string update = """
            UPDATE applications.applications
               SET first_name = @firstName,
                   last_name = @lastName
             WHERE id = @id
            """;
        await using (var cmd = new NpgsqlCommand(update, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            cmd.Parameters.AddWithValue("firstName", firstName);
            cmd.Parameters.AddWithValue("lastName", lastName);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        const string record = """
            INSERT INTO applications.overrides (application_id, kind, reason, changes)
            VALUES (@id, 'name_corrected', @reason, @changes::jsonb)
            """;
        await using (var cmd = new NpgsqlCommand(record, connection, transaction))
        {
            cmd.Parameters.AddWithValue("id", applicationId);
            cmd.Parameters.AddWithValue("reason", reason ?? string.Empty);
            cmd.Parameters.AddWithValue("changes", JsonSerializer.Serialize(changes));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<StatusChange>> HistoryOfAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT from_status, to_status, actor_id, reason, batch_id, created_at
              FROM applications.status_history
             WHERE application_id = @id
             ORDER BY created_at, id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", applicationId);

        var history = new List<StatusChange>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            history.Add(new StatusChange(
                applicationId,
                await reader.IsDBNullAsync(0, ct)
                    ? null
                    : ApplicationStatuses.Parse(reader.GetString(0)),
                ApplicationStatuses.Parse(reader.GetString(1)),
                await reader.IsDBNullAsync(2, ct) ? null : reader.GetGuid(2),
                await reader.IsDBNullAsync(3, ct) ? null : reader.GetString(3),
                await reader.IsDBNullAsync(4, ct) ? null : reader.GetGuid(4),
                reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return history;
    }

    /// <inheritdoc />
    public async Task<StoredResume?> ResumeOfAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT resume_key, resume_filename, resume_size
              FROM applications.applications
             WHERE id = @id AND resume_key IS NOT NULL
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", applicationId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new StoredResume(
            reader.GetString(0),

            // Rows written before there was anywhere to put the bytes have a
            // name and no key, so they never reach here. A key with no name is
            // not a shape that has existed, and falling back beats a null
            // reference on a screen a reviewer is trying to work through.
            await reader.IsDBNullAsync(1, ct) ? "resume.pdf" : reader.GetString(1),
            await reader.IsDBNullAsync(2, ct) ? null : reader.GetInt32(2));
    }
}
