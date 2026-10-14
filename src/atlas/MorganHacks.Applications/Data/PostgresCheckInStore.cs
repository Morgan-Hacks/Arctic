using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

/// <summary>
/// The check-in desk's read of <c>applications.*</c>.
/// </summary>
/// <remarks>
/// The code lookup runs once per person arriving, in front of a queue, on
/// whatever the venue's network is doing that morning, so it selects five
/// columns and joins nothing. The id lookup and the name search answer the
/// same question in the same five columns for the same reason: the desk
/// should not get a different record depending on which piece of glass
/// failed.
/// </remarks>
public sealed class PostgresCheckInStore(NpgsqlDataSource dataSource) : ICheckInStore
{
    private const string Columns =
        "a.id, a.status, a.first_name, a.last_name, a.checked_in_at, a.event_id, e.name";

    /// <inheritdoc />
    public async Task<CheckInSubject?> FindByCodeAsync(
        string code, CancellationToken ct = default)
    {
        // No event in the WHERE clause. The code is unique across the table
        // because a volunteer holds nothing else, and narrowing on an event
        // here would mean the endpoint guessing which year somebody meant.
        var sql = $"""
            SELECT {Columns}
              FROM applications.applications a
              LEFT JOIN applications.events e ON e.id = a.event_id
             WHERE a.check_in_code = @code
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("code", code);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, ct) : null;
    }

    /// <inheritdoc />
    public async Task<CheckInSubject?> FindAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {Columns}
              FROM applications.applications a
              LEFT JOIN applications.events e ON e.id = a.event_id
             WHERE a.id = @id
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", applicationId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, ct) : null;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CheckInSubject>> SearchAsync(
        string query, CancellationToken ct = default) =>
        SearchAsync(query, (Guid?)null, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CheckInSubject>> SearchAsync(
        string query, Guid? eventId, CancellationToken ct = default)
    {
        // Newest first rather than by name: with a year's applications in the
        // table the person in front of the desk applied this year, and the
        // ten-row cap is spent on last year's duplicates otherwise.
        //
        // The pattern is escaped rather than interpolated raw so a percent
        // sign in somebody's email searches for a percent sign. LIKE takes
        // the backslash as its escape character by default, which is why the
        // replacements below are what they are.
        var pattern = "%"
            + query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")
            + "%";

        var eventClause = eventId.HasValue ? " AND a.event_id = @eventId" : string.Empty;

        var sql = $"""
            SELECT {Columns}
              FROM applications.applications a
              LEFT JOIN applications.events e ON e.id = a.event_id
             WHERE (coalesce(a.first_name, '') || ' ' || coalesce(a.last_name, '')
                       ILIKE @pattern
                 OR a.email ILIKE @pattern){eventClause}
             ORDER BY a.created_at DESC
             LIMIT 10
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("pattern", pattern);
        if (eventId.HasValue)
        {
            cmd.Parameters.AddWithValue("eventId", eventId.Value);
        }

        var found = new List<CheckInSubject>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            found.Add(Read(reader, ct));
        }

        return found;
    }

    private static CheckInSubject Read(NpgsqlDataReader reader, CancellationToken ct) =>
        new(
            reader.GetGuid(0),
            ApplicationStatuses.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.IsDBNull(6) ? null : reader.GetString(6));
}
