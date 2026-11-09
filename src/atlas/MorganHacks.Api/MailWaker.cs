namespace MorganHacks.Api;

/// <summary>
/// Knocks on lark after mail has been queued, so it starts sending now rather
/// than whenever its scale rule next looks.
/// </summary>
/// <remarks>
/// <para>
/// lark sleeps at zero replicas and is woken by a KEDA rule that counts
/// pending rows. That rule used to run every thirty seconds, which made it the
/// way mail got noticed — and on a database that suspends when idle, a query
/// every thirty seconds is a heartbeat that stops it ever suspending. Moving
/// the rule to hourly fixes the bill and would make a sign-in link take up to
/// an hour, so something has to tell lark directly. This is that something.
/// </para>
/// <para>
/// The rule still exists at an hour, and still matters: it catches what nobody
/// knocked about — a send that failed and set <c>next_attempt_at</c>, a row
/// written by a job, a knock that did not arrive. So this is allowed to fail.
/// It is a latency optimisation over a correct backstop, never the thing
/// correctness depends on, and it must never turn a queued message into a
/// failed request.
/// </para>
/// </remarks>
public sealed class MailWaker(
    IHttpClientFactory factory,
    IConfiguration config,
    ILogger<MailWaker> log)
{
    public const string ClientName = "lark-wake";

    private readonly string? url =
        config["LARK_WAKE_URL"] ?? Environment.GetEnvironmentVariable("LARK_WAKE_URL");

    /// <summary>False in local development, where there is no lark to knock on.</summary>
    public bool Configured => !string.IsNullOrWhiteSpace(url);

    /// <summary>
    /// Posts to lark's wake endpoint, swallowing everything.
    /// </summary>
    /// <remarks>
    /// Every failure is logged at warning and none is rethrown. By the time
    /// this runs the message is committed and the response has already gone to
    /// the caller, so there is no one left to tell and nothing to undo — and
    /// the hourly rule will collect the message regardless. An exception here
    /// would surface as an unobserved task, which is noise standing in for a
    /// problem that has already handled itself.
    /// </remarks>
    public async Task KnockAsync(CancellationToken ct = default)
    {
        if (!Configured)
        {
            return;
        }

        try
        {
            using var client = factory.CreateClient(ClientName);
            using var response = await client.PostAsync(url, content: null, ct);

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning(
                    "lark answered {Status} to a wake call. Mail is queued and the "
                    + "scale rule will collect it within the hour.",
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Including the cold-start case: Container Apps brings a sleeping
            // replica up to serve this request, and if that takes longer than
            // the client timeout the knock times out having already done its
            // job. Warning rather than error for that reason.
            log.LogWarning(
                ex,
                "Could not wake lark. Mail is queued and the scale rule will "
                + "collect it within the hour.");
        }
    }
}

public static class MailWakerRegistration
{
    /// <summary>Registers <see cref="MailWaker"/> and the client it knocks with.</summary>
    /// <remarks>
    /// A short timeout on purpose. The knock is fire-and-forget after the
    /// response has been sent, so a slow one costs nothing visible — but a
    /// socket left open against a replica that is still starting is a thread
    /// and a connection held for no benefit, and the backstop already covers
    /// the case where it gives up.
    /// </remarks>
    public static IServiceCollection AddMailWaker(this IServiceCollection services)
    {
        services.AddHttpClient(MailWaker.ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddSingleton<MailWaker>();
        return services;
    }
}
