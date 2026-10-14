using System.Text.Json;
using MorganHacks.Applications.Forms;
using MorganHacks.Directory.Contracts;
using Npgsql;
namespace MorganHacks.Applications.Data;

public sealed class DirectoryApplicantSource(NpgsqlDataSource db) : IDirectoryApplicantSource
{
    public async Task<bool> IsRsvpd(Guid personId, Guid eventId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT EXISTS(SELECT 1 FROM applications.applications WHERE person_id=@person AND event_id=@event AND status IN ('confirmed','checked_in'))");
        cmd.Parameters.AddWithValue("person", personId); cmd.Parameters.AddWithValue("event", eventId); return await cmd.ExecuteScalarAsync(ct) is true;
    }
    public async Task<Guid[]> GetRsvpdPersonIds(Guid eventId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT person_id FROM applications.applications WHERE event_id=@event AND status IN ('confirmed','checked_in') AND person_id IS NOT NULL"); cmd.Parameters.AddWithValue("event", eventId);
        var ids = new List<Guid>(); await using var rows = await cmd.ExecuteReaderAsync(ct); while (await rows.ReadAsync(ct)) ids.Add(rows.GetGuid(0)); return ids.ToArray();
    }
    public async Task<ProfileFields?> GetProfileAnswers(Guid personId, Guid eventId, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT first_name,last_name,school,level_of_study,responses,first_time_hacker FROM applications.applications WHERE person_id=@person AND event_id=@event");
        cmd.Parameters.AddWithValue("person", personId); cmd.Parameters.AddWithValue("event", eventId);
        await using var row = await cmd.ExecuteReaderAsync(ct); if (!await row.ReadAsync(ct)) return null;
        string? Text(int i) => row.IsDBNull(i) ? null : row.GetString(i);
        var answers = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Text(4) ?? "{}")!;
        return DirectoryQuestions.Read(answers) with { DisplayName = string.Join(" ", new[] { Text(0), Text(1) }.Where(s => !string.IsNullOrWhiteSpace(s))), School = Text(2), YearLevel = Catalogues.ClassificationFromStudyLevel(Text(3)), FirstHackathon = row.IsDBNull(5) ? null : row.GetBoolean(5) };
    }
}
