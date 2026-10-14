using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Data;
using MorganHacks.Directory.Domain;
namespace MorganHacks.Directory;

public interface IDirectory
{
    Task OptIn(Guid personId, Guid eventId, CancellationToken ct = default);
    Task OptOut(Guid personId, Guid eventId, CancellationToken ct = default);
}
public sealed class HackerDirectory(DirectoryStore store, IDirectoryApplicantSource source) : IDirectory
{
    public async Task OptIn(Guid personId, Guid eventId, CancellationToken ct = default)
    {
        if (!await source.IsRsvpd(personId, eventId, ct)) throw new DirectoryConflict("Confirm your attendance before joining the hacker directory.");
        var original = await source.GetProfileAnswers(personId, eventId, ct) ?? new ProfileFields();
        var check = ProfileValidation.Check(original);
        // Older applications may predate catalogue validation. Keep valid fields and ask
        // the owner to complete missing ones; never publish invalid links on opt-in.
        var initial = check.Valid ? check.Value : new ProfileFields { DisplayName = original.DisplayName, School = original.School, YearLevel = original.YearLevel, FirstHackathon = original.FirstHackathon };
        await store.OptIn(personId, eventId, initial, ct);
    }
    public Task OptOut(Guid personId, Guid eventId, CancellationToken ct = default) => store.OptOut(personId, eventId, ct);
}
