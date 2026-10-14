using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using MorganHacks.Applications.Data;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using MorganHacks.Directory;
using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Data;
using Npgsql;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
namespace MorganHacks.Seed;

/// <summary>Separate local-only event; reruns preserve edited profiles and application decisions.</summary>
public sealed class DirectorySeedRunner(NpgsqlDataSource db)
{
    public static readonly Guid EventId = Guid.Parse("558164e4-fd5b-4ec8-96ef-131140fc1ccd");
    public async Task RunAsync()
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var gate = new NpgsqlCommand("SELECT pg_try_advisory_lock(728349202)", connection);
        if (await gate.ExecuteScalarAsync() is not true) throw new InvalidOperationException("Another directory seed is active.");
        try { await Seed(); } finally { await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock(728349202)", connection); await release.ExecuteScalarAsync(); }
    }
    private async Task Seed()
    {
        await using (var conflict = db.CreateCommand("SELECT EXISTS(SELECT 1 FROM applications.events WHERE (id=@id OR slug='mock-directory-v1') AND (id<>@id OR slug<>'mock-directory-v1'))")) { conflict.Parameters.AddWithValue("id", EventId); if (await conflict.ExecuteScalarAsync() is true) throw new InvalidOperationException("Reserved directory fixture event conflicts with existing data."); }
        await using (var create = db.CreateCommand("INSERT INTO applications.events(id,slug,name,decisions_announced_at) VALUES(@id,'mock-directory-v1','MOCK — Hacker directory',now()) ON CONFLICT DO NOTHING; INSERT INTO directory.settings(event_id,opens_at) VALUES(@id,now()-interval '1 day') ON CONFLICT DO NOTHING")) { create.Parameters.AddWithValue("id", EventId); await create.ExecuteNonQueryAsync(); }
        var forms = new PostgresFormStore(db); var form = (await forms.ForEventAsync(EventId)).SingleOrDefault(f => f.IsApplication) ?? await forms.CreateAsync(EventId, "MOCK directory application", "application", null);
        var version = await forms.PublishedAsync(form.Id);
        if (version is null) { await forms.DraftAsync(form.Id, null); await forms.SaveDraftAsync(form.Id, [.. SeedRunner.Fields, .. DirectoryQuestions.All]); version = await forms.PublishAsync(form.Id, null); }
        var store = new DirectoryStore(db); var directory = new HackerDirectory(store, new DirectoryApplicantSource(db)); var storage = new LocalPhotos();
        for (var i = 1; i <= 41; i++)
        {
            var email = $"mock-directory-{i:D2}@example.com";
            await using (var suppression = db.CreateCommand("INSERT INTO notify.suppressions(email,reason) VALUES(@email,'manual') ON CONFLICT(email) DO UPDATE SET reason='manual'")) { suppression.Parameters.AddWithValue("email", email); await suppression.ExecuteNonQueryAsync(); }
            Guid person;
            await using (var identity = db.CreateCommand("INSERT INTO identity.people(kind,email) VALUES('hacker',@email) ON CONFLICT(lower(email)) DO NOTHING; SELECT id FROM identity.people WHERE email=@email AND kind='hacker' AND revoked_at IS NULL")) { identity.Parameters.AddWithValue("email", email); person = await identity.ExecuteScalarAsync() is Guid id ? id : throw new InvalidOperationException("Fixture identity is revoked or belongs to an organizer."); }
            bool exists;
            await using (var current = db.CreateCommand("SELECT EXISTS(SELECT 1 FROM applications.applications WHERE event_id=@event AND email=@email)")) { current.Parameters.AddWithValue("event", EventId); current.Parameters.AddWithValue("email", email); exists = await current.ExecuteScalarAsync() is true; }
            if (!exists)
            {
                var answers = SeedRunner.Answers(i); answers["email"] = JsonSerializer.SerializeToElement(email);
                answers["directory_skills"] = JsonSerializer.SerializeToElement(new[] { Catalogues.Skills[i % Catalogues.Skills.Length], "Python" }.Distinct().ToArray());
                answers["directory_headline"] = JsonSerializer.SerializeToElement($"[Synthetic] Building useful things with new friends #{i}");
                answers["directory_contactLinks"] = JsonSerializer.SerializeToElement(new[] { "github|mock-hacker-" + i });
                answers["directory_pace"] = JsonSerializer.SerializeToElement(Catalogues.Paces[i % 3]);
                var problems = SubmissionValidation.Check(version.Fields, answers); if (problems.Count > 0) throw new InvalidOperationException("Directory fixture form no longer matches its answers.");
                var app = await new PostgresSubmissionStore(db).SubmitApplicationAsync(form, version, answers);
                await using (var link = db.CreateCommand("UPDATE applications.applications SET person_id=@person WHERE id=@id")) { link.Parameters.AddWithValue("person", person); link.Parameters.AddWithValue("id", app); await link.ExecuteNonQueryAsync(); }
                var applications = new PostgresApplicationStore(db); await applications.TransitionAsync(app, ApplicationStatus.UnderReview); await applications.TransitionAsync(app, ApplicationStatus.Accepted);
                if (i <= 40) await applications.TransitionAsync(app, ApplicationStatus.Confirmed);
            }
            if (i > 30 || await store.Mine(person, EventId, default) is not null) continue;
            await directory.OptIn(person, EventId);
            var p = (await store.Mine(person, EventId, default))!;
            var fields = p.Fields with { TeamStatus = Catalogues.TeamStatuses[i % 4], TeammatesNeeded = i % 4 == 1 ? 1 : null, FirstHackathon = i % 2 == 0 };
            if (i % 7 == 0) fields = fields with { Skills = [] };
            if (i <= 10)
            {
                using var image = new Image<Rgb24>(400, 400, new Rgb24((byte)(40 + i * 12), (byte)(80 + i * 8), (byte)(160 + i * 5)));
                using var output = new MemoryStream(); await image.SaveAsync(output, new WebpEncoder { Quality = 82 }); var photo = Guid.NewGuid();
                await store.AddPhoto(photo, person, EventId, $"directory/photos/{EventId}/{photo}.webp", output.ToArray(), storage, default); fields = fields with { PhotoId = photo };
            }
            await store.Save(person, EventId, fields, default);
            if (i is 28 or 29) await store.Moderate(EventId, person, p.Id, "hide", "Synthetic moderation example", null, default);
            if (i == 30) await directory.OptOut(person, EventId);
        }
    }
    private sealed class LocalPhotos : IPhotoStorage
    {
        private readonly BlobContainerClient container = new BlobServiceClient("UseDevelopmentStorage=true").GetBlobContainerClient("resumes");
        public async Task Put(string key, byte[] bytes, CancellationToken ct) { await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct); await container.GetBlobClient(key).UploadAsync(BinaryData.FromBytes(bytes), new BlobUploadOptions { HttpHeaders = new() { ContentType = "image/webp" } }, ct); }
        public async Task<Stream> Open(string key, CancellationToken ct) => (await container.GetBlobClient(key).DownloadStreamingAsync(cancellationToken: ct)).Value.Content;
        public async Task DeletePending(string key, CancellationToken ct) => await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
    }
}
