using System.Globalization;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Services;
using MorganHacks.Applications.Sponsors;
using Npgsql;

namespace MorganHacks.Applications.Sponsors;

/// <summary>
/// <c>sponsors.sponsors</c>, and the two aggregates built on top of it.
/// </summary>
/// <remarks>
/// Money is stripped at the read, not at the serialiser: a caller without
/// <c>sponsors.view_financials</c> gets null where the cents were, so a row
/// handed to that caller cannot leak them however it is rendered. The write
/// side refuses financial fields from the same caller rather than ignoring
/// them -- silently dropping an amount somebody typed would read as saved.
/// </remarks>
public sealed class PostgresSponsorStore(NpgsqlDataSource dataSource) : ISponsorStore
{
    private const string Columns =
        "id, event_id, name, tier, status, website_url, contact_name, contact_email, " +
        "amount_cents, created_at, updated_at";

    public async Task<IReadOnlyList<SponsorView>> ListAsync(
        Guid eventId, bool includeFinancials, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            SELECT {Columns}
              FROM sponsors.sponsors
             WHERE event_id = @eventId
             ORDER BY array_position(
                 ARRAY['platinum','gold','silver','bronze','community','in_kind'], tier),
                 lower(name)
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);

        var sponsors = new List<SponsorView>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            sponsors.Add(Read(reader, includeFinancials));
        }

        return sponsors;
    }

    public async Task<SponsorView?> CreateAsync(
        Guid eventId, NewSponsor sponsor, bool includeFinancials, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            WITH event AS (SELECT 1 FROM applications.events WHERE id = @eventId)
            INSERT INTO sponsors.sponsors
                (event_id, name, tier, status, website_url, contact_name, contact_email, amount_cents)
            SELECT @eventId, @name, @tier, @status, @websiteUrl, @contactName, @contactEmail, @amountCents
              FROM event
            RETURNING {Columns}
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("name", sponsor.Name);
        cmd.Parameters.AddWithValue("tier", sponsor.Tier);
        cmd.Parameters.AddWithValue("status", sponsor.Status);
        cmd.Parameters.Add(Text("websiteUrl", sponsor.WebsiteUrl));
        cmd.Parameters.Add(Text("contactName", sponsor.ContactName));
        cmd.Parameters.Add(Text("contactEmail", sponsor.ContactEmail));
        cmd.Parameters.Add(Amount("amountCents", sponsor.AmountCents));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, includeFinancials) : null;
    }

    public async Task<SponsorView?> UpdateAsync(
        Guid id, SponsorEdit edit, bool includeFinancials, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            UPDATE sponsors.sponsors
               SET name = COALESCE(@name, name),
                   tier = COALESCE(@tier, tier),
                   status = COALESCE(@status, status),
                   website_url = CASE WHEN @websiteSet THEN @website ELSE website_url END,
                   contact_name = CASE WHEN @contactSet THEN @contact ELSE contact_name END,
                   contact_email = CASE WHEN @emailSet THEN @email ELSE contact_email END,
                   amount_cents = CASE WHEN @amountSet THEN @amount ELSE amount_cents END
             WHERE id = @id
            RETURNING {Columns}
            """);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.Add(Text("name", edit.Name));
        cmd.Parameters.Add(Text("tier", edit.Tier));
        cmd.Parameters.Add(Text("status", edit.Status));
        cmd.Parameters.AddWithValue("websiteSet", edit.WebsiteUrl.Present);
        cmd.Parameters.Add(Text("website", edit.WebsiteUrl.Value));
        cmd.Parameters.AddWithValue("contactSet", edit.ContactName.Present);
        cmd.Parameters.Add(Text("contact", edit.ContactName.Value));
        cmd.Parameters.AddWithValue("emailSet", edit.ContactEmail.Present);
        cmd.Parameters.Add(Text("email", edit.ContactEmail.Value));
        cmd.Parameters.AddWithValue("amountSet", edit.AmountCents.Present);
        cmd.Parameters.Add(Amount("amount", edit.AmountCents.Present ? edit.AmountCents.Value : null));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, includeFinancials) : null;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "DELETE FROM sponsors.sponsors WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<SponsorReport> ReadReportAsync(Guid eventId, CancellationToken ct = default)
    {
        const string sql = """
            WITH cohort AS MATERIALIZED (
                SELECT status, school, submitted_at
                  FROM applications.applications WHERE event_id = @eventId
            )
            SELECT 'status', status, count(*) FROM cohort GROUP BY status
            UNION ALL
            SELECT 'submitted', '', count(*) FROM cohort WHERE submitted_at IS NOT NULL
            UNION ALL
            SELECT 'school', min(regexp_replace(btrim(school), '\s+', ' ', 'g')), count(*)
              FROM cohort WHERE submitted_at IS NOT NULL AND school IS NOT NULL AND btrim(school) <> ''
             GROUP BY lower(regexp_replace(btrim(school), '\s+', ' ', 'g'))
            UNION ALL
            SELECT 'tier', tier, count(*) FROM sponsors.sponsors WHERE event_id = @eventId GROUP BY tier
            """;

        var statuses = new Dictionary<string, int>();
        var schools = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tiers = new Dictionary<string, int>();
        var submitted = 0;

        await using (var command = dataSource.CreateCommand(sql))
        {
            command.Parameters.AddWithValue("eventId", eventId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var kind = reader.GetString(0);
                var label = reader.IsDBNull(1) ? "Not provided" : reader.GetString(1);
                var count = checked((int)reader.GetInt64(2));
                switch (kind)
                {
                    case "status": statuses[label] = count; break;
                    case "submitted": submitted = count; break;
                    case "school": Add(schools, string.IsNullOrWhiteSpace(label) ? "Not provided" : label, count); break;
                    case "tier": tiers[label] = count; break;
                }
            }
        }

        var total = statuses.Values.Sum();
        var buckets = Buckets(schools);
        var byTier = SponsorTiers.All
            .Where(tiers.ContainsKey)
            .Select(tier => new SponsorTierCount(tier, tiers[tier]))
            .ToArray();

        return new SponsorReport(
            eventId,
            total,
            submitted,
            statuses.GetValueOrDefault("accepted"),
            statuses.GetValueOrDefault("confirmed"),
            statuses.GetValueOrDefault("checked_in"),
            schools.Count,
            buckets.Take(10).ToArray(),
            byTier,
            null);
    }

    public async Task<SponsorFinancials> ReadFinancialsAsync(Guid eventId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT tier, status, count(*),
                   coalesce(sum(amount_cents), 0),
                   coalesce(sum(CASE WHEN status = 'paid' THEN amount_cents ELSE 0 END), 0)
              FROM sponsors.sponsors
             WHERE event_id = @eventId
             GROUP BY tier, status
            """;

        var byTier = new Dictionary<string, (int Count, long Committed, long Paid)>();
        var sponsorCount = 0;
        long committed = 0, paid = 0;

        await using (var command = dataSource.CreateCommand(sql))
        {
            command.Parameters.AddWithValue("eventId", eventId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var tier = reader.GetString(0);
                var count = checked((int)reader.GetInt64(2));
                var tierCommitted = reader.GetInt64(3);
                var tierPaid = reader.GetInt64(4);

                sponsorCount += count;
                committed += tierCommitted;
                paid += tierPaid;

                var current = byTier.GetValueOrDefault(tier);
                byTier[tier] = (current.Count + count, current.Committed + tierCommitted, current.Paid + tierPaid);
            }
        }

        return new SponsorFinancials(
            eventId,
            sponsorCount,
            committed,
            paid,
            SponsorTiers.All
                .Where(byTier.ContainsKey)
                .Select(tier =>
                {
                    var row = byTier[tier];
                    return new SponsorMoneyByTier(tier, row.Count, row.Committed, row.Paid);
                })
                .ToArray());
    }

    private static SponsorView Read(NpgsqlDataReader reader, bool includeFinancials) => new(
        reader.GetGuid(0),
        reader.GetGuid(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        includeFinancials && !reader.IsDBNull(8) ? reader.GetInt32(8) : null,
        reader.GetFieldValue<DateTimeOffset>(9),
        reader.GetFieldValue<DateTimeOffset>(10));

    private static void Add(Dictionary<string, int> counts, string label, int count) =>
        counts[label] = counts.GetValueOrDefault(label) + count;

    private static IReadOnlyList<AnalyticsBucket> Buckets(Dictionary<string, int> counts) => counts
        .OrderBy(pair => pair.Key == "Not provided").ThenByDescending(pair => pair.Value).ThenBy(pair => pair.Key)
        .Select(pair => new AnalyticsBucket(pair.Key, pair.Value)).ToArray();

    private static NpgsqlParameter Text(string name, string? value) => new(name, NpgsqlTypes.NpgsqlDbType.Text)
    {
        Value = string.IsNullOrEmpty(value) ? DBNull.Value : value,
    };

    private static NpgsqlParameter Amount(string name, int? value) => new(name, NpgsqlTypes.NpgsqlDbType.Integer)
    {
        Value = value is { } cents ? (object)cents : DBNull.Value,
    };
}
