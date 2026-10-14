using MorganHacks.Directory.Contracts;
namespace MorganHacks.Directory.Domain;

public sealed record DirectorySettings(DateTimeOffset? OpensAt, DateTimeOffset? ArchivedAt)
{
    public string Phase(DateTimeOffset now) => ArchivedAt is not null ? "archived" : OpensAt is null || OpensAt > now ? "notOpen" : "open";
}
public sealed record DirectoryProfile(Guid Id, Guid PersonId, Guid EventId, ProfileFields Fields,
    DateTimeOffset? OptedInAt, DateTimeOffset? HiddenAt, string? HiddenReason, DateTimeOffset UpdatedAt, bool PhotoRemoved)
{
    public string State => HiddenAt is not null ? "hidden" : OptedInAt is null ? "optedOut" : Fields.MissingRequiredFields().Length > 0 ? "incomplete" : "live";
}
public sealed class DirectoryConflict(string message) : Exception(message);
public sealed record Photo(Guid Id, Guid PersonId, Guid EventId, string StorageKey, string Status);
