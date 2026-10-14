using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MorganHacks.Directory;
using MorganHacks.Directory.Data;
namespace MorganHacks.Api;

public sealed class DirectoryPhotoStorage : IPhotoStorage
{
    private readonly BlobContainerClient? container;
    private readonly bool local;
    public DirectoryPhotoStorage(IConfiguration config)
    {
        var connection = config["Resumes:ConnectionString"]; var account = config["Resumes:AccountName"];
        local = !string.IsNullOrWhiteSpace(connection);
        BlobServiceClient? service = local ? new(connection) : string.IsNullOrWhiteSpace(account) ? null : new(new Uri($"https://{account}.blob.core.windows.net"), new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = config["Resumes:ClientId"] }));
        container = service?.GetBlobContainerClient(config["Resumes:Container"] ?? "resumes");
    }
    private BlobContainerClient Configured() => container ?? throw new InvalidOperationException("Photo storage is not configured.");
    public async Task Put(string key, byte[] bytes, CancellationToken ct)
    {
        var c = Configured(); if (local) await c.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        await c.GetBlobClient(key).UploadAsync(BinaryData.FromBytes(bytes), new BlobUploadOptions { HttpHeaders = new() { ContentType = "image/webp" }, Conditions = new() { IfNoneMatch = ETag.All } }, ct);
    }
    public async Task<Stream> Open(string key, CancellationToken ct) => (await Configured().GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct)).Value.Content;
    public async Task DeletePending(string key, CancellationToken ct) => await Configured().GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
}
public sealed class DirectoryPhotoCleanup(IServiceScopeFactory scopes, ILogger<DirectoryPhotoCleanup> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { using var scope = scopes.CreateScope(); var count = await scope.ServiceProvider.GetRequiredService<DirectoryStore>().Cleanup(scope.ServiceProvider.GetRequiredService<IPhotoStorage>(), stoppingToken); log.LogInformation("Pending directory photos cleaned: {Count}", count); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception) { log.LogWarning("Directory photo cleanup failed."); }
        }
    }
}
