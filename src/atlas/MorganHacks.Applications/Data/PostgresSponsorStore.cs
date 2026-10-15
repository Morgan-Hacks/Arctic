using MorganHacks.Applications.Sponsors;
using MorganHacks.Applications.Services;
using Npgsql;

namespace MorganHacks.Applications.Data;

public sealed class PostgresSponsorStore(NpgsqlDataSource dataSource) : ISponsorStore
{
    private const string Columns =
        "id, event_id, name, tier, primary_contact_name, primary_contact_email, " +
        "amount_cents, currency, committed_at, reference, created_at, updated_at";

    public async Task<IReadOnlyList<SponsorView>> ListByEventAsync(
        Guid eventId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            SELECT {Columns}
              FROM sponsors.sponsors
             WHERE event_id = @eventId
             ORDER BY lower(name)
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);

        var sponsors = new List<SponsorView>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            sponsors.Add(Read(reader));
        }

        return sponsors;
    }

    public async Task<SponsorView?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            SELECT {Columns}
              FROM sponsors.sponsors
             WHERE id = @id
            """);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return Read(reader);
    }

    public async Task<SponsorView> CreateAsync(
        Guid eventId, NewSponsor sponsor, Guid actorId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            INSERT INTO sponsors.sponsors
                (event_id, name, tier, primary_contact_name, primary_contact_email,
                 amount_cents, currency, committed_at, reference)
            VALUES
                (@eventId, @name, @tier, @contactName, @contactEmail,
                 @amount, @currency, @committedAt, @reference)
            RETURNING {Columns}
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("name", sponsor.Name);
        cmd.Parameters.AddWithValue("tier", sponsor.Tier);
        cmd.Parameters.AddWithValue("contactName", (object?)sponsor.PrimaryContactName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("contactEmail", (object?)sponsor.PrimaryContactEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("amount", (object?)sponsor.AmountCents ?? DBNull.Value);
        cmd.Parameters.AddWithValue("currency", sponsor.Currency ?? "USD");
        cmd.Parameters.AddWithValue("committedAt", (object?)sponsor.CommittedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reference", (object?)sponsor.Reference ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }

    private static SponsorView Read(NpgsqlDataReader reader)
    {
        return new SponsorView(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetFieldValue<DateTimeOffset>(10),
            reader.GetFieldValue<DateTimeOffset>(11));
    }
}
