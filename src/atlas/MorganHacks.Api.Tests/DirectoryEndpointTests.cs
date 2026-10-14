using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Directory;
using MorganHacks.Directory.Contracts;
using MorganHacks.Directory.Data;
using MorganHacks.Identity.Services;
namespace MorganHacks.Api.Tests;

public partial class DirectoryEndpointTests(ApplicationsDatabase db) : IClassFixture<ApplicationsDatabase>, IAsyncLifetime
{
    private WebApplicationFactory<Program> app = null!;
    private readonly MemoryPhotos photos = new();
    public Task InitializeAsync() { app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString); b.UseSetting("enable_hacker_portal_feature", "true"); b.UseSetting("enable_directory_feature", "true"); b.ConfigureTestServices(s => s.AddSingleton<IPhotoStorage>(photos)); }); return Task.CompletedTask; }
    public Task DisposeAsync() { app.Dispose(); return Task.CompletedTask; }
    private DirectoryStore Store => new(db.DataSource);
    private async Task<(Guid Person, Guid Event, string Cookie)> Hacker(string status = "confirmed", Guid? eventId = null)
    {
        var e = eventId ?? await db.AddEventAsync(); var email = $"directory-{Guid.NewGuid():N}@example.com";
        await using var person = db.DataSource.CreateCommand("INSERT INTO identity.people(kind,email) VALUES('hacker',@email) RETURNING id"); person.Parameters.AddWithValue("email", email); var id = (Guid)(await person.ExecuteScalarAsync())!;
        await using var a = db.DataSource.CreateCommand("""
            INSERT INTO applications.applications(event_id,person_id,email,status,first_name,last_name,age,phone,school,level_of_study,graduation_year,first_time_hacker,country,mlh_coc_agreed_at,mlh_data_sharing_at,responses)
            VALUES(@event,@person,@email,@status,'Ada','Lovelace',20,'5555555555','Morgan','undergraduate-3y',2028,true,'US',now(),now(),'{"directory_skills":["Python"],"directory_contactLinks":["github|ada"],"directory_headline":"Build with me","directory_pronouns":"she/her","directory_buildIdea":"A tool","directory_lookingFor":"Designers","directory_interests":["Education"],"directory_pace":"Here to learn"}');
            UPDATE applications.events SET decisions_announced_at=now()-interval '1 day' WHERE id=@event;
            INSERT INTO directory.settings(event_id,opens_at) VALUES(@event,now()-interval '1 day') ON CONFLICT DO NOTHING;
            """); a.Parameters.AddWithValue("event", e); a.Parameters.AddWithValue("person", id); a.Parameters.AddWithValue("email", email); a.Parameters.AddWithValue("status", status); await a.ExecuteNonQueryAsync();
        return (id, e, await Cookie(id));
    }
    private async Task<string> Cookie(Guid person) { using var scope = app.Services.CreateScope(); return "mh_session=" + await scope.ServiceProvider.GetRequiredService<SessionService>().StartAsync(person); }
    private Task<HttpResponseMessage> Send(string method, string path, string? cookie = null, object? body = null)
    { var request = new HttpRequestMessage(new HttpMethod(method), path); if (cookie is not null) request.Headers.Add("Cookie", cookie); if (body is not null) request.Content = JsonContent.Create(body); return app.CreateClient(new() { HandleCookies = false }).SendAsync(request); }
    private static async Task<JsonElement> Body(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    private async Task Join(string cookie) => (await Send("POST", "/portal/directory/profile/opt-in", cookie)).EnsureSuccessStatusCode();
    private sealed class MemoryPhotos : IPhotoStorage
    {
        private readonly Dictionary<string, byte[]> files = [];
        public Task Put(string key, byte[] bytes, CancellationToken ct) { files[key] = bytes; return Task.CompletedTask; }
        public Task<Stream> Open(string key, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(files[key]));
        public Task DeletePending(string key, CancellationToken ct) { files.Remove(key); return Task.CompletedTask; }
    }
}
