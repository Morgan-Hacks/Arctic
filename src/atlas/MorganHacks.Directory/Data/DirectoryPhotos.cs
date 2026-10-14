using MorganHacks.Directory.Domain;
using Npgsql;
namespace MorganHacks.Directory.Data;

public sealed partial class DirectoryStore
{
    public async Task<Photo?> FindPhoto(Guid id, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT id,person_id,event_id,storage_key,status FROM directory.photos WHERE id=@id"); cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct); return await r.ReadAsync(ct) ? new(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3), r.GetString(4)) : null;
    }
    public async Task AddPhoto(Guid id, Guid person, Guid eventId, string key, byte[] bytes, IPhotoStorage storage, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct); await Lock(c, tx, eventId, person, ct);
        await using var count = new NpgsqlCommand("SELECT count(*) FROM directory.photos WHERE person_id=@person AND created_at>now()-interval '1 hour'", c, tx); count.Parameters.AddWithValue("person", person);
        if ((long)(await count.ExecuteScalarAsync(ct))! >= 10) throw new DirectoryConflict("You have reached the photo upload limit. Try again in an hour.");
        await using var cmd = new NpgsqlCommand("INSERT INTO directory.photos(id,person_id,event_id,storage_key,width,height,bytes,status) VALUES(@id,@person,@event,@key,400,400,@bytes,'pending')", c, tx);
        cmd.Parameters.AddWithValue("id", id); cmd.Parameters.AddWithValue("person", person); cmd.Parameters.AddWithValue("event", eventId); cmd.Parameters.AddWithValue("key", key); cmd.Parameters.AddWithValue("bytes", bytes.Length);
        await cmd.ExecuteNonQueryAsync(ct);
        await storage.Put(key, bytes, ct);
        // A failed commit response can be ambiguous. Never delete a blob that another
        // request could already have attached after a successful database commit.
        await tx.CommitAsync(ct);
    }
    public async Task<int> Cleanup(IPhotoStorage storage, CancellationToken ct)
    {
        await using var c = await db.OpenConnectionAsync(ct); await using var tx = await c.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT id,storage_key FROM directory.photos WHERE status='pending' AND created_at<now()-interval '24 hours' FOR UPDATE SKIP LOCKED", c, tx);
        var pending = new List<(Guid Id, string Key)>(); await using (var r = await cmd.ExecuteReaderAsync(ct)) { while (await r.ReadAsync(ct)) pending.Add((r.GetGuid(0), r.GetString(1))); }
        foreach (var p in pending) { await storage.DeletePending(p.Key, ct); await using var delete = new NpgsqlCommand("DELETE FROM directory.photos WHERE id=@id AND status='pending'", c, tx); delete.Parameters.AddWithValue("id", p.Id); await delete.ExecuteNonQueryAsync(ct); }
        await tx.CommitAsync(ct); return pending.Count;
    }
}
