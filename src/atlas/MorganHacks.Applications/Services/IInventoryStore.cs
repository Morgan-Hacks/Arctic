using MorganHacks.Applications.Logistics;

namespace MorganHacks.Applications.Services;

/// <summary>
/// What logistics has, where it is, and how much of it is left.
/// </summary>
/// <remarks>
/// Scoped to an event everywhere an item is read or written, matching
/// <c>logistics.items.event_id</c> — last year's shirts are not this year's.
/// <para>
/// <see cref="MoveAsync"/> is the one method on this interface that is not a
/// plain CRUD operation, and it is the reason this interface exists at all.
/// A count and the ledger explaining it must never disagree, which means the
/// row has to be locked, read, checked and written in one transaction — see
/// <c>PostgresInventoryStore</c> for how, and <c>0051_inventory</c> for why
/// the table shape makes that the only place this can go wrong.
/// </para>
/// </remarks>
public interface IInventoryStore
{
    /// <summary>Every item for one event, by name.</summary>
    Task<IReadOnlyList<InventoryItemView>> ListAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>
    /// Adds an item. When <paramref name="item"/> carries a nonzero count,
    /// also writes the opening movement that explains it, in the same
    /// transaction as the row.
    /// </summary>
    Task<InventoryItemView> CreateAsync(
        Guid eventId, NewInventoryItem item, Guid actorId, CancellationToken ct = default);

    /// <summary>Applies an edit, or null when there is no such item.</summary>
    Task<InventoryItemView?> UpdateAsync(
        Guid id, InventoryItemEdit edit, CancellationToken ct = default);

    /// <summary>True if a row was removed, false when there was no such item.</summary>
    Task<InventoryDeletion> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Moves the count by <paramref name="delta"/> and appends the ledger row
    /// that explains it.
    /// </summary>
    /// <remarks>
    /// The item row is locked with <c>SELECT ... FOR UPDATE</c> before the
    /// balance is computed, so two callers moving the same item at once are
    /// serialised rather than both reading the count that was true before
    /// either of them acted. <paramref name="delta"/> that would take the
    /// count below zero is refused before the write — see
    /// <see cref="InventoryMoveOutcome.InsufficientStock"/> — rather than left
    /// for <c>CHECK (count &gt;= 0)</c> to catch, because a constraint
    /// violation is a 500 and this has to be a sentence.
    /// </remarks>
    Task<InventoryMovementResult> MoveAsync(
        Guid id, int delta, string? reason, Guid actorId, CancellationToken ct = default);

    /// <summary>
    /// The ledger for one item, newest first, or null when there is no such
    /// item.
    /// </summary>
    Task<IReadOnlyList<InventoryMovementView>?> MovementsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Which event an item belongs to, and what photo it has now.</summary>
    /// <remarks>
    /// Read before an upload rather than after, because the event is part of
    /// the storage key. Null when there is no such item, which is what lets the
    /// endpoint answer 404 before it has read a single byte of the file.
    /// </remarks>
    Task<InventoryImageTarget?> ImageTargetAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Points an item at a photo, or at none, and says what it pointed at before.
    /// </summary>
    /// <remarks>
    /// The previous key comes back so the caller can delete a replaced photo.
    /// Doing it here would mean this class knew about blob storage, which it
    /// does not and should not -- it owns rows.
    /// </remarks>
    Task<InventoryImageSwap?> SetImageAsync(
        Guid id, string? key, CancellationToken ct = default);
}
