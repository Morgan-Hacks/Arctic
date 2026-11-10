using System.Reflection;
using DbUp;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MorganHacks.Lark.Tests;

public sealed class NotifyDatabase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:18-alpine").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var cs = _container.GetConnectionString();

        await using (var bootstrap = NpgsqlDataSource.Create(cs))
        await using (var cmd = bootstrap.CreateCommand(
            "CREATE SCHEMA IF NOT EXISTS identity;" +
            "CREATE SCHEMA IF NOT EXISTS applications;" +
            "CREATE SCHEMA IF NOT EXISTS profiles;" +
            "CREATE SCHEMA IF NOT EXISTS notify;"))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // The real migrations, so a broken one fails these tests too.
        var result = DeployChanges.To.PostgresqlDatabase(cs)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetAssembly(typeof(MorganHacks.Migrations.MigrationsAssemblyMarker))!)
            .WithTransactionPerScript().Build().PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException($"Migrations failed: {result.Error}");
        }

        DataSource = NpgsqlDataSource.Create(cs);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    /// <summary>Creates a campaign and returns its id.</summary>
    /// <summary>
    /// A campaign whose template carries a sender name, and whose own settings
    /// are whatever a test hands it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="AddCampaignAsync"/> because what is being
    /// tested is the interaction between the two: a template that names a
    /// sender and a campaign that does not, which is the shape every campaign
    /// has when nobody filled the field in.
    /// </remarks>
    public async Task<Guid> AddNamedSenderCampaignAsync(
        string? templateFromName, string? settingsJson)
    {
        await using var cmd = DataSource.CreateCommand(Q);

        cmd.Parameters.AddWithValue("fromName", (object?)templateFromName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("settings", (object?)settingsJson ?? DBNull.Value);

        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    private const string Q = @"
            WITH t AS (
              INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text,
                 from_local, from_domain, from_name)
              VALUES (gen_random_uuid()::text, 'broadcast', 's', '<p>h</p>', 't',
                      'mail', 'morganhacks.test', @fromName)
              RETURNING id
            )
            INSERT INTO notify.campaigns (template_id, name, status, email_settings)
            SELECT id, 'test', 'queued', @settings::jsonb FROM t
            RETURNING id";

    public async Task<Guid> AddCampaignAsync(
        string kind = "transactional", string? replyTo = null)
    {
        await using var cmd = DataSource.CreateCommand("""
            WITH t AS (
              INSERT INTO notify.templates
                (key, kind, subject, body_html, body_text,
                 from_local, from_domain, reply_to)
              VALUES (gen_random_uuid()::text, @kind, 's', '<p>h</p>', 't',
                      'no-reply', 'auth.example.com', @replyTo)
              RETURNING id
            )
            INSERT INTO notify.campaigns (template_id, name, status)
            SELECT id, 'test', 'queued' FROM t
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("replyTo", (object?)replyTo ?? DBNull.Value);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// The person a message points at, created on first use.
    /// </summary>
    /// <remarks>
    /// Idempotent because one address is one person: queueing two messages to
    /// the same recipient is normal, and the unique index on lower(email)
    /// means a second insert would fail rather than give back the row that
    /// already exists.
    /// </remarks>
    public async Task<Guid> AddPersonAsync(string email)
    {
        await using var cmd = DataSource.CreateCommand("""
            INSERT INTO identity.people (kind, email) VALUES ('hacker', @e)
            ON CONFLICT (lower(email)) DO UPDATE SET updated_at = now()
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("e", email);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Queues one message. Priority 0 is transactional, 10 broadcast.</summary>
    public async Task<Guid> QueueAsync(Guid campaignId, string email, short priority = 10)
    {
        var personId = await AddPersonAsync(email);
        await using var cmd = DataSource.CreateCommand("""
            INSERT INTO notify.messages
              (campaign_id, person_id, to_email, priority,
               rendered_subject, rendered_body_html, rendered_body_text)
            VALUES (@c, @person, @e, @p, 'subject', '<p>body</p>', 'body')
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("person", personId);
        cmd.Parameters.AddWithValue("c", campaignId);
        cmd.Parameters.AddWithValue("e", email);
        cmd.Parameters.AddWithValue("p", priority);
        return (Guid)(await cmd.ExecuteScalarAsync())!;
    }

    public async Task<(string Status, short Attempts, DateTimeOffset? NextAttempt)> StateOf(Guid id)
    {
        await using var cmd = DataSource.CreateCommand(
            "SELECT status, attempts, next_attempt_at FROM notify.messages WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetString(0), r.GetInt16(1),
            await r.IsDBNullAsync(2) ? null : r.GetFieldValue<DateTimeOffset>(2));
    }

    /// <summary>Pushes a message's next attempt into the future.</summary>
    /// <remarks>
    /// A message in backoff is still work the queue owes someone, it is just
    /// not work yet. Separate from <see cref="ExpireLockAsync"/>, which is
    /// about a worker that died holding a claim.
    /// </remarks>
    public async Task DeferAsync(Guid id, TimeSpan by)
    {
        await using var cmd = DataSource.CreateCommand(
            "UPDATE notify.messages SET next_attempt_at = now() + @by WHERE id = @id");
        cmd.Parameters.AddWithValue("by", by);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ExpireLockAsync(Guid id)
    {
        await using var cmd = DataSource.CreateCommand(
            "UPDATE notify.messages SET locked_until = now() - interval '1 minute' WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
