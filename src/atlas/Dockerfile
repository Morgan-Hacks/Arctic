# atlas — the API.
#
# Built from the repository root rather than from src/atlas, because atlas
# references libs/observability, libs/audit and lark's data library. A context
# of src/atlas cannot see any of them, and the alternative — copying them in
# beforehand — is a build that works in CI and not on a laptop.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so restore is cached against dependency changes rather
# than against every source edit. This is most of the build time.
COPY src/atlas/MorganHacks.Api/MorganHacks.Api.csproj                 src/atlas/MorganHacks.Api/
COPY src/atlas/MorganHacks.Identity/MorganHacks.Identity.csproj       src/atlas/MorganHacks.Identity/
COPY src/atlas/MorganHacks.Applications/MorganHacks.Applications.csproj src/atlas/MorganHacks.Applications/
COPY src/atlas/MorganHacks.Directory/MorganHacks.Directory.csproj src/atlas/MorganHacks.Directory/
COPY libs/directory/MorganHacks.Directory.Contracts/MorganHacks.Directory.Contracts.csproj \
     libs/directory/MorganHacks.Directory.Contracts/
COPY src/atlas/MorganHacks.Profiles/MorganHacks.Profiles.csproj       src/atlas/MorganHacks.Profiles/
COPY src/atlas/MorganHacks.Migrations/MorganHacks.Migrations.csproj   src/atlas/MorganHacks.Migrations/
COPY src/lark/MorganHacks.Lark.Data/MorganHacks.Lark.Data.csproj      src/lark/MorganHacks.Lark.Data/
COPY libs/observability/MorganHacks.Observability/MorganHacks.Observability.csproj \
     libs/observability/MorganHacks.Observability/
COPY libs/audit/MorganHacks.Audit/MorganHacks.Audit.csproj \
     libs/audit/MorganHacks.Audit/
COPY libs/features/MorganHacks.Features/MorganHacks.Features.csproj \
     libs/features/MorganHacks.Features/
COPY libs/directory/MorganHacks.Directory.Images/MorganHacks.Directory.Images.csproj libs/directory/MorganHacks.Directory.Images/
COPY global.json ./
COPY src/atlas/Directory.Build.props src/atlas/
COPY src/lark/Directory.Build.props  src/lark/
COPY libs/Directory.Build.props      libs/
RUN dotnet restore src/atlas/MorganHacks.Api/MorganHacks.Api.csproj

COPY src/atlas/ src/atlas/
COPY src/lark/MorganHacks.Lark.Data/ src/lark/MorganHacks.Lark.Data/
COPY libs/ libs/
RUN dotnet publish src/atlas/MorganHacks.Api/MorganHacks.Api.csproj \
    -c Release -o /app --no-restore

# DB-IP keeps only the last couple of monthly releases and deletes the rest, so
# this pin stops being downloadable rather than merely going stale: 2026-08 and
# 2026-09 are both 404 now. Expect to bump it every month or two, and note that a
# cached layer hides the breakage -- the build that broke on this was the first
# one in a while to miss the cache.
ARG COUNTRY_DATABASE_RELEASE=2026-11
ARG COUNTRY_DATABASE_SHA256=75fe430010f6d38a8c4331c202bf0df6ce3221a6e6d823b9e5a6d951e4cf9758
RUN curl --fail --location --retry 3 --max-time 120 \
      "https://download.db-ip.com/free/dbip-country-lite-${COUNTRY_DATABASE_RELEASE}.mmdb.gz" -o /tmp/country.mmdb.gz \
 && printf '%s  %s\n' "$COUNTRY_DATABASE_SHA256" /tmp/country.mmdb.gz | sha256sum -c - \
 && mkdir -p /app/data \
 && gzip -dc /tmp/country.mmdb.gz > /app/data/country.mmdb \
 && rm /tmp/country.mmdb.gz

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Two things in one layer, because they share the apt index and dropping it
# twice would cost a second copy of it in the image.
#
# The upgrade is the security one. The base image is rebuilt on Microsoft's
# schedule, and the distribution publishes fixes on its own, so between those
# two dates the image carries packages that Ubuntu has already patched --
# which is a Trivy HIGH and a failed build for every branch that touches a
# backend, whatever the branch actually changed. Taking the upgrade here means
# the image is current as of the build rather than as of whenever the base was
# last pushed.
#
# It is deliberately `upgrade` and not a named package. Pinning the CVE of the
# week fixes that CVE and leaves the next one to block somebody else's pull
# request for reasons that have nothing to do with their work.
#
# The cost is honest: two builds of the same commit can differ, because the
# archive moves underneath them. That is the trade -- reproducibility for
# currency -- and it is the right way round here, where images are tagged by
# commit and rebuilt on every deploy, and where the alternative is shipping
# known-vulnerable OpenSSL until somebody upstream gets to it.
#
# libgssapi-krb5-2 is the functional one. Npgsql probes for Kerberos support on
# every connection. Without this library it still connects, but prints "Cannot
# load library libgssapi_krb5.so.2" to stderr at every startup — a permanent
# error-shaped line in the logs for a feature we do not use, which is how
# people learn to ignore real ones.
RUN apt-get update \
 && apt-get upgrade -y --no-install-recommends \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .
ENV EmailTracking__CountryDatabasePath=/app/data/country.mmdb

# Non-root. The image ships an unprivileged user for exactly this, and a
# container that does not need root should not have it.
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "MorganHacks.Api.dll"]
