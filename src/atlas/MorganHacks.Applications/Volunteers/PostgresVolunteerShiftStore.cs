using MorganHacks.Applications.Services;
using MorganHacks.Applications.Volunteers;
using Npgsql;

namespace MorganHacks.Applications.Volunteers;

/// <summary>
/// <c>volunteers.shifts</c> and <c>volunteers.assignments</c>, read together.
/// </summary>
/// <remarks>
/// One query per list, not one per shift: the coverage screen reads the whole
/// rota for an event at once, and forty shifts must not cost forty round
/// trips. Assignments are matched to their shift in memory, in shift order,
/// so a shift with nobody on it still arrives -- with an empty list rather
/// than absent, because an uncovered shift missing from the response reads
/// as covered.
/// </remarks>
public sealed class PostgresVolunteerShiftStore(NpgsqlDataSource dataSource) : IVolunteerShiftStore
{
    private const string ShiftColumns =
        "id, event_id, name, starts_at, ends_at, needed, created_at, updated_at";

    public async Task<IReadOnlyList<ShiftView>> ListAsync(Guid eventId, CancellationToken ct = default)
    {
        var shifts = new List<ShiftRow>();
        await using (var cmd = dataSource.CreateCommand($"""
            SELECT {ShiftColumns}
              FROM volunteers.shifts
             WHERE event_id = @eventId
             ORDER BY starts_at NULLS LAST, lower(name)
            """))
        {
            cmd.Parameters.AddWithValue("eventId", eventId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                shifts.Add(ReadShift(reader));
            }
        }

        if (shifts.Count == 0)
        {
            return [];
        }

        var byShift = shifts.ToDictionary(s => s.Id, _ => new List<AssignmentView>());
        var ids = shifts.Select(s => s.Id).ToArray();
        await using (var cmd = dataSource.CreateCommand("""
            SELECT id, shift_id, volunteer_name, volunteer_email, created_at
              FROM volunteers.assignments
             WHERE shift_id = ANY(@shiftIds)
             ORDER BY lower(volunteer_name)
            """))
        {
            cmd.Parameters.AddWithValue("shiftIds", ids);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var assignment = new AssignmentView(
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4));
                if (byShift.TryGetValue(assignment.ShiftId, out var list))
                {
                    list.Add(assignment);
                }
            }
        }

        return shifts
            .Select(s => new ShiftView(
                s.Id, s.EventId, s.Name, s.StartsAt, s.EndsAt, s.Needed,
                s.CreatedAt, s.UpdatedAt, byShift[s.Id]))
            .ToList();
    }

    public async Task<ShiftView?> CreateAsync(Guid eventId, NewShift shift, Guid actorId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            WITH event AS (SELECT 1 FROM applications.events WHERE id = @eventId)
            INSERT INTO volunteers.shifts (event_id, name, starts_at, ends_at, needed, created_by)
            SELECT @eventId, @name, @startsAt, @endsAt, @needed, @actorId
              FROM event
            RETURNING {ShiftColumns}
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("name", shift.Name);
        cmd.Parameters.Add(Instant("startsAt", shift.StartsAt));
        cmd.Parameters.Add(Instant("endsAt", shift.EndsAt));
        cmd.Parameters.AddWithValue("needed", shift.Needed);
        cmd.Parameters.AddWithValue("actorId", actorId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var row = ReadShift(reader);
        return new ShiftView(
            row.Id, row.EventId, row.Name, row.StartsAt, row.EndsAt, row.Needed,
            row.CreatedAt, row.UpdatedAt, []);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "DELETE FROM volunteers.shifts WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<AssignmentView?> AssignAsync(
        Guid shiftId, NewAssignment assignment, Guid actorId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand("""
            WITH shift AS (SELECT 1 FROM volunteers.shifts WHERE id = @shiftId)
            INSERT INTO volunteers.assignments (shift_id, volunteer_name, volunteer_email, created_by)
            SELECT @shiftId, @name, @email, @actorId
              FROM shift
            RETURNING id, shift_id, volunteer_name, volunteer_email, created_at
            """);
        cmd.Parameters.AddWithValue("shiftId", shiftId);
        cmd.Parameters.AddWithValue("name", assignment.VolunteerName);
        cmd.Parameters.Add(Text("email", assignment.VolunteerEmail));
        cmd.Parameters.AddWithValue("actorId", actorId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new AssignmentView(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    public async Task<bool> UnassignAsync(Guid shiftId, Guid assignmentId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "DELETE FROM volunteers.assignments WHERE id = @id AND shift_id = @shiftId");
        cmd.Parameters.AddWithValue("id", assignmentId);
        cmd.Parameters.AddWithValue("shiftId", shiftId);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private sealed record ShiftRow(
        Guid Id,
        Guid EventId,
        string Name,
        DateTimeOffset? StartsAt,
        DateTimeOffset? EndsAt,
        int Needed,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private static ShiftRow ReadShift(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
        reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
        reader.GetInt32(5),
        reader.GetFieldValue<DateTimeOffset>(6),
        reader.GetFieldValue<DateTimeOffset>(7));

    private static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlTypes.NpgsqlDbType.Text)
    {
        Value = value is null ? DBNull.Value : value,
    };

    // Npgsql writes timestamptz from a UTC DateTimeOffset only; the instant
    // an organizer typed in the event's zone is the same moment, converted.
    // See PostgresEventStore, which does the same on the way to its date
    // columns, for the reason this normalises here rather than at the
    // endpoint: every write below goes through this one helper.
    private static NpgsqlParameter Instant(string name, DateTimeOffset? value) => new(name, NpgsqlTypes.NpgsqlDbType.TimestampTz)
    {
        Value = value is null ? DBNull.Value : value.Value.ToUniversalTime(),
    };
}
