namespace MorganHacks.Directory;

public interface IPhotoStorage
{
    Task Put(string key, byte[] bytes, CancellationToken ct);
    Task<Stream> Open(string key, CancellationToken ct);
    Task DeletePending(string key, CancellationToken ct);
}
