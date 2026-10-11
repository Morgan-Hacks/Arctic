using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using MorganHacks.Applications.Services;

namespace MorganHacks.Applications.Storage;

/// <summary>Where the inventory photos live.</summary>
/// <remarks>
/// A second container in the same account as the resumes, not a second
/// account. The Blob Data Contributor grant in platform.bicep is scoped to the
/// account, so this needs no role assignment of its own -- and a role
/// assignment on a resource group that did not have one is how the first
/// production deploy failed.
/// </remarks>
public sealed record InventoryImageOptions
{
    public string? AccountName { get; init; }

    /// <summary>Azurite locally. Never set against Azure.</summary>
    public string? ConnectionString { get; init; }

    public string Container { get; init; } = "inventory";

    /// <summary>The user-assigned identity atlas runs as.</summary>
    public string? ManagedIdentityClientId { get; init; }
}

/// <inheritdoc />
public sealed class AzureInventoryImageStore : IInventoryImageStore
{
    private readonly BlobServiceClient? _service;
    private readonly BlobContainerClient? _container;

    public AzureInventoryImageStore(InventoryImageOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            _service = new BlobServiceClient(options.ConnectionString);
        }
        else if (!string.IsNullOrWhiteSpace(options.AccountName))
        {
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = options.ManagedIdentityClientId,
            });

            _service = new BlobServiceClient(
                new Uri($"https://{options.AccountName}.blob.core.windows.net"), credential);
        }

        _container = _service?.GetBlobContainerClient(options.Container);
    }

    public bool Available => _container is not null;

    public async Task<string> StoreAsync(
        Guid eventId, Guid itemId, ReadOnlyMemory<byte> content, string contentType,
        CancellationToken ct = default)
    {
        if (_container is null)
        {
            throw new InvalidOperationException("No storage account is configured for inventory photos.");
        }

        var key = Domain.InventoryImage.NewKey(eventId, itemId, contentType);
        var blob = _container.GetBlobClient(key);

        // The content type is the one the header proved, never the one the
        // upload claimed. A file stored as the type it says it is, having been
        // checked only on its extension, is how a browser is talked into
        // running something.
        await blob.UploadAsync(
            BinaryData.FromBytes(content),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);

        return key;
    }

    public async Task<SignedImage?> LinkToAsync(string key, CancellationToken ct = default)
    {
        if (_container is null)
        {
            return null;
        }

        var blob = _container.GetBlobClient(key);
        var expiresAt = DateTimeOffset.UtcNow + IInventoryImageStore.LinkLifetime;

        var sas = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = key,
            Resource = "b",
            ExpiresOn = expiresAt,
        };

        sas.SetPermissions(BlobSasPermissions.Read);

        if (blob.Uri.Scheme == Uri.UriSchemeHttps)
        {
            sas.Protocol = SasProtocol.Https;
        }

        // Azurite can sign with the account key it was built from; Azure
        // cannot, because allowSharedKeyAccess is false. There the signature
        // comes from a user delegation key, which is the identity's own and
        // expires with it.
        if (blob.CanGenerateSasUri)
        {
            return new SignedImage(blob.GenerateSasUri(sas), expiresAt);
        }

        // Clock skew either side, so a link is not refused for being a few
        // seconds in the future against a server that disagrees.
        var skew = TimeSpan.FromMinutes(5);
        var delegation = await _service!.GetUserDelegationKeyAsync(
            DateTimeOffset.UtcNow - skew, expiresAt + skew, ct);

        var query = sas.ToSasQueryParameters(delegation, _service.AccountName);
        return new SignedImage(new BlobUriBuilder(blob.Uri) { Sas = query }.ToUri(), expiresAt);
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        if (_container is null)
        {
            return;
        }

        try
        {
            await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
        }
        catch (RequestFailedException)
        {
            // Swallowed by contract. The item row is already gone, so there is
            // nobody to tell and nothing to retry for -- an orphaned blob costs
            // a fraction of a penny, and an exception here would turn a
            // completed delete into a 500.
        }
    }
}
