using System.Text.Json;
using MorganHacks.Directory.Domain;
using Npgsql;
namespace MorganHacks.Directory.Data;

public sealed record BrowseQuery(string? Q = null, string[]? Skills = null, string? Status = null, string? Pace = null, bool? FirstTimer = null);
public sealed record DirectoryCursor(int Priority, DateTimeOffset UpdatedAt, Guid Id)
{
    public string Encode() => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this));
    public static DirectoryCursor? Decode(string? text)
    {
        if (text is null) return null;
        if (text.Length > 1000) throw new FormatException();
        try { return JsonSerializer.Deserialize<DirectoryCursor>(Convert.FromBase64String(text)) ?? throw new FormatException(); }
        catch (JsonException) { throw new FormatException(); }
    }
    public static int Rank(DirectoryProfile p) => p.Fields.TeamStatus is "looking" or "needMembers" ? 0 : 1;
}
public sealed partial class DirectoryStore
{
    public async Task<IReadOnlyList<DirectoryProfile>> Browse(Guid eventId, Guid[]? eligible, BrowseQuery filter, DirectoryCursor? after, int limit, CancellationToken ct)
    {
        var sql = Select + " WHERE p.event_id=@event";
        if (eligible is not null) sql += " AND p.person_id=ANY(@eligible) AND p.opted_in_at IS NOT NULL AND p.hidden_at IS NULL AND p.is_complete";
        if (!string.IsNullOrEmpty(filter.Q)) sql += " AND (p.display_name ILIKE @q ESCAPE '~' OR p.headline ILIKE @q ESCAPE '~' OR p.school ILIKE @q ESCAPE '~' OR p.build_idea ILIKE @q ESCAPE '~' OR EXISTS(SELECT 1 FROM unnest(p.skills) skill WHERE skill ILIKE @q ESCAPE '~'))";
        if (filter.Skills?.Length > 0) sql += " AND p.skills && @skills";
        if (filter.Status is not null) sql += " AND p.team_status=@status";
        if (filter.Pace is not null) sql += " AND p.pace=@pace";
        if (filter.FirstTimer is not null) sql += " AND p.first_hackathon=@first";
        const string rank = "(CASE WHEN p.team_status IN ('looking','needMembers') THEN 0 ELSE 1 END)";
        if (after is not null) sql += $" AND ({rank}>@rank OR ({rank}=@rank AND (p.updated_at,p.id)<(@at,@id)))";
        sql += $" ORDER BY {rank},p.updated_at DESC,p.id DESC LIMIT @limit";
        await using var cmd = db.CreateCommand(sql); cmd.Parameters.AddWithValue("event", eventId); cmd.Parameters.AddWithValue("limit", limit);
        if (eligible is not null) cmd.Parameters.AddWithValue("eligible", eligible);
        if (!string.IsNullOrEmpty(filter.Q)) cmd.Parameters.AddWithValue("q", "%" + filter.Q.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_") + "%");
        if (filter.Skills?.Length > 0) cmd.Parameters.AddWithValue("skills", filter.Skills);
        if (filter.Status is not null) cmd.Parameters.AddWithValue("status", filter.Status);
        if (filter.Pace is not null) cmd.Parameters.AddWithValue("pace", filter.Pace);
        if (filter.FirstTimer is not null) cmd.Parameters.AddWithValue("first", filter.FirstTimer.Value);
        if (after is not null) { cmd.Parameters.AddWithValue("rank", after.Priority); cmd.Parameters.AddWithValue("at", after.UpdatedAt); cmd.Parameters.AddWithValue("id", after.Id); }
        var rows = new List<DirectoryProfile>(); await using var r = await cmd.ExecuteReaderAsync(ct); while (await r.ReadAsync(ct)) rows.Add(Read(r)); return rows;
    }
    public async Task Moderate(Guid eventId, Guid actor, Guid? profileId, string action, string? reason, DateTimeOffset? opensAt, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct); await Lock(c, tx, eventId, actor, ct);
        var sql = action switch
        {
            "hide" => "UPDATE directory.profiles SET hidden_at=now(),hidden_by=@actor,hidden_reason=@reason,updated_at=now() WHERE id=@id AND event_id=@event",
            "unhide" => "UPDATE directory.profiles SET hidden_at=NULL,hidden_by=NULL,hidden_reason=NULL,updated_at=now() WHERE id=@id AND event_id=@event",
            "remove-photo" => "UPDATE directory.photos SET status='removed',removed_by=@actor WHERE id=(SELECT photo_id FROM directory.profiles WHERE id=@id AND event_id=@event); UPDATE directory.profiles SET photo_id=NULL,updated_at=now() WHERE id=@id AND event_id=@event",
            "settings" => "UPDATE directory.settings SET opens_at=@opens WHERE event_id=@event",
            "archive" => "UPDATE directory.settings SET archived_at=now(),archived_by=@actor WHERE event_id=@event",
            _ => throw new ArgumentException("Unknown action")
        };
        await using var cmd = new NpgsqlCommand(sql, c, tx); cmd.Parameters.AddWithValue("event", eventId); cmd.Parameters.AddWithValue("actor", actor);
        cmd.Parameters.AddWithValue("id", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)profileId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reason", NpgsqlTypes.NpgsqlDbType.Text, (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("opens", NpgsqlTypes.NpgsqlDbType.TimestampTz, (object?)opensAt ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
}
