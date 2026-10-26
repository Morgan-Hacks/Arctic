using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MorganHacks.Features;

/// <summary>
/// The names of the flags. One place, so a typo is a build error.
/// </summary>
/// <remarks>
/// The convention is <c>enable_thing_feature</c>, and it is the same string in
/// every service and every frontend -- the C# constant, the key in features.json,
/// the TypeScript constant, and the environment variable that overrides it (which
/// is the same name upper-cased, because .NET treats configuration keys
/// case-insensitively and the frontends upper-case it themselves).
/// </remarks>
public static class Flags
{
    public const string Directory = "enable_directory_feature";
    /// <summary>The applicant portal: /portal in the web app, /portal/* in the API.</summary>
    public const string HackerPortal = "enable_hacker_portal_feature";
}

/// <summary>
/// Whether a feature is on, asked fresh every time.
/// </summary>
public interface IFeatures
{
    bool IsOn(string flag);
}

internal sealed class ConfigurationFeatures(IConfiguration configuration) : IFeatures
{
    /// <summary>
    /// Read through to configuration on every call rather than cached.
    ///
    /// features.json is registered with reloadOnChange, so editing the file moves
    /// the flag without a restart. Caching here would quietly take that away, and
    /// the failure would look like the file not working.
    /// </summary>
    public bool IsOn(string flag) => configuration.GetValue(flag, defaultValue: false);
}

public static class FeatureExtensions
{
    /// <summary>
    /// Load features.json and make <see cref="IFeatures"/> available.
    /// </summary>
    /// <remarks>
    /// The file is not optional. A service whose flag file is missing would read
    /// every flag as off and serve a stripped-down version of itself, quietly, in
    /// production -- so a missing file stops the process instead.
    ///
    /// Environment variables are added again afterwards so they sit above the file.
    /// The host adds them before this call, and the last source added wins, so
    /// without this line the file would override the variable and the emergency
    /// lever would not work.
    /// </remarks>
    public static IHostApplicationBuilder AddFeatures(this IHostApplicationBuilder builder)
    {
        var files = new PhysicalFileProvider(builder.Environment.ContentRootPath);

        var source = new JsonConfigurationSource
        {
            Path = "features.json",
            Optional = false,
            ReloadOnChange = true,
            FileProvider = files,
        };

        // First, so it is the lowest priority source rather than the highest.
        //
        // The file states what the flags are when nobody has said otherwise. Every
        // other mechanism has to be able to beat it, or the emergency lever does
        // not work: appending it instead puts it above the environment variables
        // the host already added, and ENABLE_HACKER_PORTAL_FEATURE=false in a
        // container would be read and then silently overruled by the file baked
        // into the image beside it.
        builder.Configuration.Sources.Insert(0, source);

        // Then the overlay, at 1: above the base file and still below the
        // environment variables the host added before this call.
        //
        // features.json is committed, so it holds one value per *branch*. main is
        // mirrored onto staging and also drives the production deploy, which means
        // the base file cannot say "on in staging, off in production" -- both are
        // built from the same tree. The only per-environment lever used to be a
        // variable set in a portal somewhere, which works and is invisible:
        // nothing in the repository tells you what is on in production, and a flag
        // left on is precisely the thing nobody notices.
        //
        // The overlay puts that answer back in git as a reviewable diff, and it is
        // a shallow merge: it names only the flags that differ, so a key it leaves
        // out keeps the base value rather than becoming false.
        //
        // Optional, because most environments will not have one, and an absent
        // overlay means "nothing differs here" rather than a misconfiguration. The
        // base file stays non-optional for the opposite reason -- a service with no
        // flag file at all would read every flag as off and quietly serve a
        // stripped-down version of itself.
        //
        // Lower-cased, so the filename is the same string in every service.
        // EnvironmentName is `Staging` here and the frontends are handed `staging`
        // by Vercel; a repository holding both features.Staging.json and
        // features.staging.json is one where somebody edits the wrong file.
        var environment = builder.Environment.EnvironmentName.ToLowerInvariant();
        builder.Configuration.Sources.Insert(1, new JsonConfigurationSource
        {
            Path = $"features.{environment}.json",
            Optional = true,
            ReloadOnChange = true,
            FileProvider = files,
        });

        builder.Services.AddSingleton<IFeatures, ConfigurationFeatures>();
        return builder;
    }

    /// <summary>
    /// Hide a group of endpoints while its flag is off.
    /// </summary>
    /// <remarks>
    /// 404 rather than 403. A feature that is off should be indistinguishable from
    /// a feature that was never built: 403 tells somebody there is something there
    /// and they are not allowed it, which is a different, wrong sentence. It also
    /// matches what the frontend does, which is send them to the home page.
    /// </remarks>
    public static TBuilder RequireFeature<TBuilder>(this TBuilder builder, string flag)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
        {
            var features = context.HttpContext.RequestServices.GetRequiredService<IFeatures>();
            return features.IsOn(flag)
                ? await next(context)
                : Results.NotFound();
        });
        return builder;
    }
}
