using MorganHacks.Applications.Services;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Logistics;

/// <summary>
/// <c>logistics.items</c> and <c>logistics.movements</c>, kept in step.
/// </summary>
/// <remarks>
/// <see cref="MoveAsync"/> is the one method here that opens its own
/// connection and transaction rather than letting <c>NpgsqlDataSource</c>
/// hand out a command per call, for the reason <c>PostgresAnnouncementReactionStore</c>
/// does the same: a read, a check and two writes have to be one unit, and the
/// item row has to be locked for the width of all four. <see cref="CreateAsync"/>
/// does the same for a narrower reason — an opening movement is optional, but
/// when it happens it has to land with the row or not at all.
/// </remarks>
public sealed class PostgresInventoryStore(NpgsqlDataSource dataSource) : IInventoryStore
{
    /// <summary>
    /// Every column a view needs, in the order <see cref="Read"/> reads them
    /// by position.
    /// </summary>
    private const string Columns =
        "id, name, category, location, note, low_at, count, image_key, updated_at";

    public async Task<IReadOnlyList<InventoryItemView>> ListAsync(
        Guid eventId, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand($"""
            SELECT {Columns}
              FROM logistics.items
             WHERE event_id = @eventId
             ORDER BY lower(name)
            """);
        cmd.Parameters.AddWithValue("eventId", eventId);

        var items = new List<InventoryItemView>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(Read(reader));
        }

        return items;
    }

    public async Task<InventoryItemView> CreateAsync(
        Guid eventId, NewInventoryItem item, Guid actorId, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        InventoryItemView created;
        await using (var insert = new NpgsqlCommand($"""
            INSERT INTO logistics.items (event_id, name, category, location, note, low_at, count)
            VALUES (@eventId, @name, @category, @location, @note, @lowAt, @count)
            RETURNING {Columns}
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("eventId", eventId);
            insert.Parameters.AddWithValue("name", item.Name);
            insert.Parameters.Add(Text("category", item.Category));
            insert.Parameters.Add(Text("location", item.Location));
            insert.Parameters.Add(Text("note", item.Note));
            insert.Parameters.Add(new NpgsqlParameter("lowAt", NpgsqlDbType.Integer)
            {
                Value = item.LowAt is { } low ? (object)low : DBNull.Value,
            });
            insert.Parameters.AddWithValue("count", item.Count);

            await using var reader = await insert.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            created = Read(reader);
        }

        // A nonzero starting count gets an opening movement in the same
        // transaction as the row, so the ledger explains the whole number
        // from the first line rather than beginning with an unexplained
        // forty. Zero is skipped rather than written as a no-op movement —
        // movements.delta has CHECK (delta <> 0), and a count of zero needs
        // no explaining anyway.
        if (item.Count != 0)
        {
            await using var movement = new NpgsqlCommand("""
                INSERT INTO logistics.movements (item_id, delta, balance, reason, actor_id)
                VALUES (@itemId, @delta, @balance, @reason, @actorId)
                """, connection, transaction);
            movement.Parameters.AddWithValue("itemId", created.Id);
            movement.Parameters.AddWithValue("delta", item.Count);
            movement.Parameters.AddWithValue("balance", item.Count);
            movement.Parameters.AddWithValue("reason", "Starting count.");
            movement.Parameters.AddWithValue("actorId", actorId);
            await movement.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return created;
    }

    public async Task<InventoryItemView?> UpdateAsync(
        Guid id, InventoryItemEdit edit, CancellationToken ct = default)
    {
        // One statement with a CASE per optional column, the same shape
        // PostgresEventStore.UpdateAsync uses and for the same reason:
        // assembling SQL from whichever fields a request happened to carry is
        // how a column name ends up coming from a caller.
        //
        // COALESCE for the name, because the column is NOT NULL and there is
        // no clearing it, only correcting it. CASE for the rest, because each
        // of them can be unset and a patch that did not name one has to leave
        // it exactly where it was.
        await using var cmd = dataSource.CreateCommand($"""
            UPDATE logistics.items
               SET name = COALESCE(@name, name),
                   category = CASE WHEN @categorySet THEN @category ELSE category END,
                   location = CASE WHEN @locationSet THEN @location ELSE location END,
                   note = CASE WHEN @noteSet THEN @note ELSE note END,
                   low_at = CASE WHEN @lowAtSet THEN @lowAt ELSE low_at END
             WHERE id = @id
            RETURNING {Columns}
            """);

        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text)
        {
            Value = (object?)edit.Name ?? DBNull.Value,
        });
        AddText(cmd, "category", edit.Category);
        AddText(cmd, "location", edit.Location);
        AddText(cmd, "note", edit.Note);
        AddInt(cmd, "lowAt", edit.LowAt);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// Removes an item, and says which photo is now unreferenced.
    /// </summary>
    /// <remarks>
    /// Returns the image key rather than deleting the blob here, because this
    /// class owns rows and not storage. The caller deletes the blob after the
    /// row is gone.
    /// <para>
    /// That order is deliberate. A blob deleted first and a row delete that
    /// then fails leaves an item pointing at a photo that is not there, which
    /// shows up as a broken image on the one screen somebody uses to count
    /// boxes. The other way round leaves a file nobody references, which costs
    /// a fraction of a penny and nothing else. RETURNING makes it one
    /// statement, so there is no window where the row is gone and the key has
    /// been forgotten.
    /// </para>
    /// </remarks>
    public async Task<InventoryDeletion> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        // ON DELETE CASCADE takes the item's movements with it. The ledger
        // for a thing that no longer exists has nothing left to explain.
        await using var cmd = dataSource.CreateCommand(
            "DELETE FROM logistics.items WHERE id = @id RETURNING image_key");
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new InventoryDeletion(false, null);
        }

        return new InventoryDeletion(true, reader.IsDBNull(0) ? null : reader.GetString(0));
    }

    public async Task<InventoryMovementResult> MoveAsync(
        Guid id, int delta, string? reason, Guid actorId, CancellationToken ct = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // Locked before anything is read. Without FOR UPDATE, two people
        // handing out shirts at once can both read a count of forty and both
        // decide thirty-nine is the right balance to write — the second
        // write would be correct against a row that no longer exists by the
        // time it lands. The lock serialises them instead: the second
        // transaction blocks here until the first commits, and then reads
        // the balance the first one actually left behind.
        int current;
        string name;
        await using (var read = new NpgsqlCommand("""
            SELECT count, name FROM logistics.items WHERE id = @id FOR UPDATE
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("id", id);
            await using var reader = await read.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return new InventoryMovementResult(InventoryMoveOutcome.NotFound, null, null);
            }

            current = reader.GetInt32(0);
            name = reader.GetString(1);
        }

        var balance = current + delta;
        if (balance < 0)
        {
            // Caught here, against the row this transaction is holding
            // locked, rather than left for CHECK (count >= 0) to catch on
            // the write below. The constraint would answer with a 500 and
            // a SQLSTATE; this answers with the number the caller actually
            // needs to see.
            return new InventoryMovementResult(
                InventoryMoveOutcome.InsufficientStock,
                null,
                $"Only {current} \"{name}\" left.");
        }

        await using (var write = new NpgsqlCommand("""
            UPDATE logistics.items SET count = @balance WHERE id = @id
            """, connection, transaction))
        {
            write.Parameters.AddWithValue("id", id);
            write.Parameters.AddWithValue("balance", balance);
            await write.ExecuteNonQueryAsync(ct);
        }

        // The balance stored here is the one just computed from the locked
        // row, never anything the caller sent — see IInventoryStore.MoveAsync.
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO logistics.movements (item_id, delta, balance, reason, actor_id)
            VALUES (@itemId, @delta, @balance, @reason, @actorId)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("itemId", id);
            insert.Parameters.AddWithValue("delta", delta);
            insert.Parameters.AddWithValue("balance", balance);
            insert.Parameters.Add(Text("reason", reason));
            insert.Parameters.AddWithValue("actorId", actorId);
            await insert.ExecuteNonQueryAsync(ct);
        }

        InventoryItemView item;
        await using (var reread = new NpgsqlCommand($"""
            SELECT {Columns} FROM logistics.items WHERE id = @id
            """, connection, transaction))
        {
            reread.Parameters.AddWithValue("id", id);
            await using var reader = await reread.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            item = Read(reader);
        }

        await transaction.CommitAsync(ct);
        return new InventoryMovementResult(InventoryMoveOutcome.Ok, item, null);
    }

    public async Task<IReadOnlyList<InventoryMovementView>?> MovementsAsync(
        Guid id, CancellationToken ct = default)
    {
        await using var exists = dataSource.CreateCommand(
            "SELECT 1 FROM logistics.items WHERE id = @id");
        exists.Parameters.AddWithValue("id", id);
        if (await exists.ExecuteScalarAsync(ct) is null)
        {
            return null;
        }

        // The actor is resolved here, against identity.people, rather than
        // left as an id for the console to look up. There is no second
        // screen this ledger could send that lookup to — unlike the audit
        // trail, which deliberately leaves the join to /admin/people because
        // that screen is gated separately. Null survives a removed person:
        // actor_id is ON DELETE SET NULL, by design — see 0051_inventory.
        await using var cmd = dataSource.CreateCommand("""
            SELECT m.id, m.delta, m.balance, m.reason, m.occurred_at,
                   COALESCE(p.full_name, p.email)
              FROM logistics.movements m
              LEFT JOIN identity.people p ON p.id = m.actor_id
             WHERE m.item_id = @id
             ORDER BY m.id DESC
            """);
        cmd.Parameters.AddWithValue("id", id);

        var movements = new List<InventoryMovementView>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            movements.Add(new InventoryMovementView(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return movements;
    }

    private static InventoryItemView Read(NpgsqlDataReader reader)
    {
        var lowAt = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
        var count = reader.GetInt32(6);

        return new InventoryItemView(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            lowAt,
            count,
            lowAt is not null && count <= lowAt,

            // Null here, filled by the endpoint. Signing a link is a call to
            // storage, and a store that reached for it would turn listing
            // forty items into forty round trips before the first one drew.
            null,
            reader.GetFieldValue<DateTimeOffset>(8))
        {
            ImageKey = reader.IsDBNull(7) ? null : reader.GetString(7),
        };
    }

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static void AddText(NpgsqlCommand cmd, string name, TextPatch patch)
    {
        cmd.Parameters.AddWithValue($"{name}Set", patch.Present);
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?)patch.Value ?? DBNull.Value,
        });
    }

    private static void AddInt(NpgsqlCommand cmd, string name, Patch<int> patch)
    {
        cmd.Parameters.AddWithValue($"{name}Set", patch.Present);
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Integer)
        {
            Value = patch.Value is { } value ? (object)value : DBNull.Value,
        });
    }

    public async Task<InventoryImageTarget?> ImageTargetAsync(
        Guid id, CancellationToken ct = default)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT event_id, image_key FROM logistics.items WHERE id = @id");
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new InventoryImageTarget(
            reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    public async Task<InventoryImageSwap?> SetImageAsync(
        Guid id, string? key, CancellationToken ct = default)
    {
        // One statement, so there is no window where the new key is written
        // and the old one has been forgotten. A crash between two statements
        // would leave a blob nobody can find to delete.
        // The previous key comes from a CTE rather than a subquery in
        // RETURNING. A subquery against the table being updated reads the
        // statement's snapshot, which is the sort of thing that works until
        // it does not; a CTE is materialised before the UPDATE and is
        // well-defined. One statement either way, so there is no window where
        // the new key is written and the old one has been forgotten.
        await using var cmd = dataSource.CreateCommand($"""
            WITH previous AS (
                SELECT image_key FROM logistics.items WHERE id = @id
            )
            UPDATE logistics.items
               SET image_key = @key
             WHERE id = @id
            RETURNING {Columns}, (SELECT image_key FROM previous) AS previous_key
            """);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("key", (object?)key ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var previous = reader.IsDBNull(reader.FieldCount - 1)
            ? null
            : reader.GetString(reader.FieldCount - 1);

        return new InventoryImageSwap(Read(reader), previous);
    }
}
