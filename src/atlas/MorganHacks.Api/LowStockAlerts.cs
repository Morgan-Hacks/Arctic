using System.Globalization;
using MorganHacks.Lark.Data.Data;
using Npgsql;

namespace MorganHacks.Api;

/// <summary>Moves durable inventory crossings into Lark's retryable email queue.</summary>
public sealed class LowStockAlerts(
    NpgsqlDataSource dataSource, TemplateStore templates, MessageQueue queue,
    IConfiguration config, ILogger<LowStockAlerts> log)
{
    public async Task<bool> DispatchNextAsync(CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        long alertId;
        Guid eventId;
        string itemName, eventName;
        string? location;
        int balance, threshold;
        bool enabled;
        await using (var cmd = new NpgsqlCommand("""
            SELECT a.id, a.event_id, a.item_name, a.location, a.balance, a.low_at,
                   e.name, i.low_at IS NOT NULL
              FROM logistics.low_stock_alerts a
              JOIN logistics.items i ON i.id = a.item_id
              JOIN applications.events e ON e.id = a.event_id
             WHERE a.processed_at IS NULL
             ORDER BY a.id LIMIT 1 FOR UPDATE OF a SKIP LOCKED
            """, connection, transaction))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            alertId = reader.GetInt64(0);
            eventId = reader.GetGuid(1);
            itemName = reader.GetString(2);
            location = reader.IsDBNull(3) ? null : reader.GetString(3);
            balance = reader.GetInt32(4);
            threshold = reader.GetInt32(5);
            eventName = reader.GetString(6);
            enabled = reader.GetBoolean(7);
        }

        var recipients = new List<(Guid Id, string Email)>();
        if (enabled)
        {
            // EXISTS deduplicates overlapping team membership and direct grants.
            // Match the permission gate's expiry/revocation rules, and the
            // transactional queue's suppression rules (unsubscribe is broadcast-only).
            await using var cmd = new NpgsqlCommand("""
                SELECT p.id, p.email FROM identity.people p
                 WHERE p.kind = 'organizer' AND p.revoked_at IS NULL
                   AND (EXISTS (
                       SELECT 1 FROM identity.team_members m
                       JOIN identity.team_permissions t ON t.team_id = m.team_id
                        WHERE m.person_id = p.id AND t.permission = 'inventory.manage'
                          AND (m.expires_at IS NULL OR m.expires_at > now()))
                     OR EXISTS (
                       SELECT 1 FROM identity.grants g
                        WHERE g.person_id = p.id AND g.permission = 'inventory.manage'
                          AND (g.expires_at IS NULL OR g.expires_at > now())))
                   AND NOT EXISTS (
                       SELECT 1 FROM notify.suppressions s WHERE s.email = p.email
                         AND s.reason <> 'unsubscribed')
                """, connection, transaction);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                recipients.Add((reader.GetGuid(0), reader.GetString(1)));
        }

        if (recipients.Count > 0)
        {
            var template = await templates.FindAsync("inventory_low_stock", ct);
            if (template is null || !template.IsTransactional)
                throw new InvalidOperationException("The transactional inventory_low_stock template is missing.");

            var values = new Dictionary<string, string>
            {
                ["item"] = itemName,
                ["event"] = eventName,
                ["location"] = location ?? "Not specified",
                ["count"] = balance.ToString(CultureInfo.InvariantCulture),
                ["threshold"] = threshold.ToString(CultureInfo.InvariantCulture),
                ["inventory"] = $"{Origins.Console(config)}/inventory?eventId={eventId}",
            };
            foreach (var recipient in recipients)
                await queue.EnqueueTransactionalAsync(connection, transaction, template,
                    recipient.Email, recipient.Id, values, ct: ct);
        }

        await using (var done = new NpgsqlCommand("""
            UPDATE logistics.low_stock_alerts SET processed_at = now() WHERE id = @id
            """, connection, transaction))
        {
            done.Parameters.AddWithValue("id", alertId);
            await done.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);

        if (enabled && recipients.Count == 0)
            log.LogWarning("Low-stock alert had no eligible recipients. {alert_id}", alertId);
        else
            log.LogInformation("Low-stock alert processed. {alert_id} {recipient_count}", alertId, recipients.Count);
        return true;
    }
}

public sealed class LowStockAlertWorker(LowStockAlerts alerts, ILogger<LowStockAlertWorker> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                for (var i = 0; i < 20 && await alerts.DispatchNextAsync(stoppingToken); i++) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Low-stock alert dispatch failed; pending alerts will be retried.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
