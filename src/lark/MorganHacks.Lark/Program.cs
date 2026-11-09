using Amazon.SimpleEmailV2;
using MorganHacks.Lark;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Sending;
using MorganHacks.Observability;
using Npgsql;
using MorganHacks.Features;

// A web application, for a worker that serves exactly one request.
//
// lark has no API and wants none. What it needs is a door somebody can knock
// on, because SendLoop below is a BackgroundService: it starts draining the
// queue the moment this process exists, so *being started* is the entire
// behaviour. Container Apps' HTTP scaler brings a sleeping replica up to
// answer a request, which turns "wake the mail worker" into a plain POST
// instead of a database poll running every thirty seconds forever.
var builder = WebApplication.CreateBuilder(args);

builder.UseArcticLogging("lark");

var connectionString =
    builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("ARCTIC_DB")
    ?? "Host=localhost;Port=5432;Database=morganhacks;Username=arctic;Password=local-dev-only";

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<MessageQueue>();
builder.Services.AddSingleton<LinkTrackingStore>();
builder.Services.AddSingleton<UnsubscribeStore>();
builder.Services.AddSingleton<TemplateStore>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<SendLoopOptions>(builder.Configuration.GetSection("SendLoop"));

// Region and credentials come from the environment, so nothing secret is in
// the repo and the same binary runs locally, on staging and in production.
//
// Constructing the SES client without a region throws, so an unset variable
// would take the whole worker down at startup and keep it down — a crash loop
// that reports a dependency-injection stack trace rather than "no region
// configured". Checked here instead, and the worker runs either way.
var awsRegion = builder.Configuration["AWS_REGION"]
                ?? Environment.GetEnvironmentVariable("AWS_REGION");

// Which configuration set each send is tagged with, and therefore whether
// SES reports anything back about it. Optional for the same reason the region
// is checked rather than assumed: a worker that refuses to start because event
// publishing is not set up yet is worse than one that sends without it.
//
// The cost of leaving it unset is not nothing, and is worth saying out loud —
// bounces and complaints never arrive, so notify.suppressions stays empty of
// real ones and every message stops at 'sent'. See docs/runbooks.
var sesConfigurationSet = builder.Configuration["SES_CONFIGURATION_SET"]
                          ?? Environment.GetEnvironmentVariable("SES_CONFIGURATION_SET");

if (!string.IsNullOrWhiteSpace(awsRegion))
{
    builder.Services.AddSingleton<IAmazonSimpleEmailServiceV2>(
        _ => new AmazonSimpleEmailServiceV2Client(
            Amazon.RegionEndpoint.GetBySystemName(awsRegion)));

    builder.Services.AddSingleton<IEmailProvider>(sp => new SesEmailProvider(
        sp.GetRequiredService<IAmazonSimpleEmailServiceV2>(),
        sp.GetRequiredService<ILogger<SesEmailProvider>>(),
        string.IsNullOrWhiteSpace(sesConfigurationSet) ? null : sesConfigurationSet.Trim()));

    if (string.IsNullOrWhiteSpace(sesConfigurationSet))
    {
        // Loud once at startup rather than silent forever. The symptom
        // otherwise is every message sitting at 'sent' and nobody wondering
        // why none ever reaches 'delivered'.
        Console.WriteLine(
            "SES_CONFIGURATION_SET is not set, so SES will report no bounces, "
            + "complaints or deliveries and addresses will not be suppressed.");
    }
}
else
{
    builder.Services.AddSingleton<IEmailProvider, UnconfiguredEmailProvider>();
}

builder.Services.AddHostedService<SendLoop>();

// Feature flags. None are read here yet; the call is what makes adding the
// first one a one-line change, and what makes a missing features.json fail
// at start-up rather than at the moment somebody first relies on a flag.
builder.AddFeatures();

var app = builder.Build();

// The door. It deliberately does nothing.
//
// There is no body to read and no message id to pass: by the time this handler
// runs, the process is up and SendLoop is already claiming rows. Accepting a
// payload would invite a caller to believe this dispatches one specific
// message, which it does not and should not -- the queue decides what is next,
// including retries that no caller knows about.
//
// 202 rather than 200 for the same reason: the work has been accepted, not
// completed, and the caller must not wait around for it.
app.MapPost("/api/wake", () => Results.Accepted());

// Liveness for the platform's own probe, kept separate from the door above so
// that a health check never looks like a knock in the logs.
app.MapGet("/api/health", () => Results.Ok("ok"));

app.Run();
