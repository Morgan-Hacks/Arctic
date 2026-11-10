using Npgsql;

namespace MorganHacks.Seed;

public static class SeedSafety
{
    // Staging's database is a Neon project, and both of its endpoints address the
    // same database: the pooled one is what the services use, the direct one is
    // what migrations and dumps use. The seed job is handed whichever the
    // environment exports, so both are allowed.
    //
    // Neon calls every project's database `neondb` and its role `neondb_owner`.
    // Unlike the Azure servers, where staging and production differed in host and
    // the names were ours to choose, those two values no longer tell the two
    // environments apart -- the host is the only thing that does. That is why it is
    // compared exactly, and not by prefix or suffix: `-pooler` sits in the middle of
    // the hostname, so a pattern meant to match one endpoint quietly matches both.
    private const string PooledHost = "ep-calm-recipe-bdcyjjj3-pooler.c-8.us-east-2.aws.neon.tech";
    private const string DirectHost = "ep-calm-recipe-bdcyjjj3.c-8.us-east-2.aws.neon.tech";

    public static void CheckEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        foreach (var key in new[] { "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT" })
        {
            var value = read(key);
            if (!string.IsNullOrEmpty(value) && value != "Development")
                throw new InvalidOperationException($"Refusing seed: {key} must be unset or Development.");
        }
        var target = read("ARCTIC_TARGET");
        if (!string.IsNullOrEmpty(target) && target != "local")
            throw new InvalidOperationException("Refusing seed: ARCTIC_TARGET must be local.");
        if (!string.IsNullOrEmpty(read("ARCTIC_DB")))
            throw new InvalidOperationException("Unset ARCTIC_DB before seeding. Only the verified local Compose database is supported.");
    }
    public static string StagingConnection(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        if (read("ARCTIC_TARGET") != "staging" || read("DOTNET_ENVIRONMENT") != "Staging"
            || read("ASPNETCORE_ENVIRONMENT") is { Length: > 0 } webEnvironment && webEnvironment != "Staging")
            throw new InvalidOperationException("Staging seeding requires ARCTIC_TARGET=staging and DOTNET_ENVIRONMENT=Staging.");
        NpgsqlConnectionStringBuilder connection;
        try { connection = new(read("ARCTIC_DB") ?? ""); }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid staging database configuration."); }
        if ((connection.Host != PooledHost && connection.Host != DirectHost)
            || connection.Port != 5432 || connection.Database != "neondb"
            || connection.Username != "neondb_owner" || connection.SslMode != SslMode.VerifyFull
            || string.IsNullOrEmpty(connection.Password))
            throw new InvalidOperationException("Refusing seed: only the verified-TLS MorganHacks staging database is allowed.");
        return connection.ConnectionString;
    }
}
