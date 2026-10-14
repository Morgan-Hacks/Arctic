using System.Text.Json;
using MorganHacks.Audit;
using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Domain;
using Npgsql;
namespace MorganHacks.Directory.Data;

public sealed partial class DirectoryStore(NpgsqlDataSource db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Select = """
      SELECT p.id,p.person_id,p.event_id,jsonb_build_object('displayName', p.display_name, 'school', p.school, 'yearLevel', p.year_level, 'firstHackathon', p.first_hackathon, 'pronouns', p.pronouns, 'headline', p.headline, 'buildIdea', p.build_idea, 'lookingFor', p.looking_for, 'pace', p.pace, 'teamStatus', p.team_status, 'teammatesNeeded', p.teammates_needed, 'skills', p.skills, 'interests', p.interests, 'contactLinks', p.contact_links, 'photoId', p.photo_id),p.opted_in_at,p.hidden_at,p.hidden_reason,p.updated_at,
        EXISTS(SELECT 1 FROM directory.photos ph WHERE ph.person_id=p.person_id AND ph.event_id=p.event_id AND ph.status='removed') AND p.photo_id IS NULL
      FROM directory.profiles p
      """;
    private static DirectoryProfile Read(NpgsqlDataReader r) => new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2),
        JsonSerializer.Deserialize<ProfileFields>(r.GetString(3), Json)!,
        r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4), r.IsDBNull(5) ? null : r.GetFieldValue<DateTimeOffset>(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.GetFieldValue<DateTimeOffset>(7), r.GetBoolean(8));
    public async Task<DirectorySettings> Settings(Guid eventId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT opens_at,archived_at FROM directory.settings WHERE event_id=@event");
        cmd.Parameters.AddWithValue("event", eventId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? new(r.IsDBNull(0) ? null : r.GetFieldValue<DateTimeOffset>(0), r.IsDBNull(1) ? null : r.GetFieldValue<DateTimeOffset>(1)) : new(null, null);
    }
    public async Task<DirectoryProfile?> Mine(Guid person, Guid eventId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(Select + " WHERE p.person_id=@person AND p.event_id=@event");
        cmd.Parameters.AddWithValue("person", person); cmd.Parameters.AddWithValue("event", eventId);
        await using var r = await cmd.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? Read(r) : null;
    }
    public async Task<DirectoryProfile?> Find(Guid id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(Select + " WHERE p.id=@id"); cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? Read(r) : null;
    }
    // Serialize writes against archive and each other, including the initial settings insert.
    private static async Task Lock(NpgsqlConnection c, NpgsqlTransaction tx, Guid eventId, Guid actor, CancellationToken ct)
    {
        await AuditContext.SetActorAsync(c, tx, actor, ct);
        await using var cmd = new NpgsqlCommand("INSERT INTO directory.settings(event_id) VALUES(@event) ON CONFLICT DO NOTHING; SELECT archived_at IS NOT NULL FROM directory.settings WHERE event_id=@event FOR UPDATE", c, tx);
        cmd.Parameters.AddWithValue("event", eventId);
        if (await cmd.ExecuteScalarAsync(ct) is true) throw new DirectoryConflict("The directory has closed. Profiles are read-only.");
    }
    public async Task OptIn(Guid person, Guid eventId, ProfileFields initial, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await Lock(c, tx, eventId, person, ct);
        await using var cmd = new NpgsqlCommand("INSERT INTO directory.profiles(person_id,event_id) VALUES(@person,@event) ON CONFLICT DO NOTHING RETURNING id", c, tx);
        cmd.Parameters.AddWithValue("person", person); cmd.Parameters.AddWithValue("event", eventId);
        if (await cmd.ExecuteScalarAsync(ct) is Guid) await SaveFields(c, tx, person, eventId, initial, ct);
        await using var opt = new NpgsqlCommand("UPDATE directory.profiles SET opted_in_at=coalesce(opted_in_at,now()) WHERE person_id=@person AND event_id=@event", c, tx);
        opt.Parameters.AddWithValue("person", person); opt.Parameters.AddWithValue("event", eventId); await opt.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task OptOut(Guid person, Guid eventId, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct); await Lock(c, tx, eventId, person, ct);
        await using var cmd = new NpgsqlCommand("UPDATE directory.profiles SET opted_in_at=NULL WHERE person_id=@person AND event_id=@event", c, tx);
        cmd.Parameters.AddWithValue("person", person); cmd.Parameters.AddWithValue("event", eventId); await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task Save(Guid person, Guid eventId, ProfileFields fields, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct); await Lock(c, tx, eventId, person, ct);
        await using var current = new NpgsqlCommand("SELECT photo_id FROM directory.profiles WHERE person_id=@person AND event_id=@event FOR UPDATE", c, tx);
        current.Parameters.AddWithValue("person", person); current.Parameters.AddWithValue("event", eventId);
        var old = await current.ExecuteScalarAsync(ct);
        if (old is null) throw new DirectoryConflict("Join the directory before saving a profile.");
        var oldId = old as Guid?;
        if (fields.PhotoId != oldId)
        {
            if (fields.PhotoId is Guid id)
            {
                await using var attach = new NpgsqlCommand("UPDATE directory.photos SET status='attached',attached_at=now() WHERE id=@id AND person_id=@person AND event_id=@event AND status='pending'", c, tx);
                attach.Parameters.AddWithValue("id", id); attach.Parameters.AddWithValue("person", person); attach.Parameters.AddWithValue("event", eventId);
                if (await attach.ExecuteNonQueryAsync(ct) != 1) throw new DirectoryConflict("Choose one of your own pending photos.");
            }
            if (oldId is Guid previous)
            {
                await using var detach = new NpgsqlCommand("UPDATE directory.photos SET status='detached' WHERE id=@id AND status='attached'", c, tx); detach.Parameters.AddWithValue("id", previous); await detach.ExecuteNonQueryAsync(ct);
            }
        }
        await SaveFields(c, tx, person, eventId, fields, ct); await tx.CommitAsync(ct);
    }
    private static async Task SaveFields(NpgsqlConnection c, NpgsqlTransaction tx, Guid person, Guid eventId, ProfileFields f, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE directory.profiles SET display_name=@DisplayName, school=@School, year_level=@YearLevel, first_hackathon=@FirstHackathon, pronouns=@Pronouns, headline=@Headline, build_idea=@BuildIdea, looking_for=@LookingFor, pace=@Pace, team_status=@TeamStatus, teammates_needed=@TeammatesNeeded, skills=@Skills, interests=@Interests, contact_links=@ContactLinks, photo_id=@PhotoId, is_complete=@complete,updated_at=now() WHERE person_id=@person AND event_id=@event", c, tx);
        cmd.Parameters.AddWithValue("person", person); cmd.Parameters.AddWithValue("event", eventId); cmd.Parameters.AddWithValue("complete", f.MissingRequiredFields().Length == 0);
        cmd.Parameters.AddWithValue("DisplayName", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("School", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.School ?? DBNull.Value);
        cmd.Parameters.AddWithValue("YearLevel", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.YearLevel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("FirstHackathon", NpgsqlTypes.NpgsqlDbType.Boolean, (object?)f.FirstHackathon ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Pronouns", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.Pronouns ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Headline", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.Headline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("BuildIdea", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.BuildIdea ?? DBNull.Value);
        cmd.Parameters.AddWithValue("LookingFor", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.LookingFor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Pace", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.Pace ?? DBNull.Value);
        cmd.Parameters.AddWithValue("TeamStatus", NpgsqlTypes.NpgsqlDbType.Text, (object?)f.TeamStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("TeammatesNeeded", NpgsqlTypes.NpgsqlDbType.Integer, (object?)f.TeammatesNeeded ?? DBNull.Value);
        cmd.Parameters.AddWithValue("Skills", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, f.Skills);
        cmd.Parameters.AddWithValue("Interests", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, f.Interests);
        cmd.Parameters.AddWithValue("ContactLinks", NpgsqlTypes.NpgsqlDbType.Jsonb, JsonSerializer.Serialize(f.ContactLinks, Json));
        cmd.Parameters.AddWithValue("PhotoId", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)f.PhotoId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
