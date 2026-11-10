#!/usr/bin/env bash
#
# Deploys one environment through main.bicep, in the only order that is safe.
#
#   ./deploy.sh staging                     # what-if: show the diff, change nothing
#   ./deploy.sh staging --apply             # do it
#   ./deploy.sh staging --apply <tag>       # deploy an older tag — this is a rollback
#
# Bicep describes what should exist. This handles the two things that are
# genuinely a sequence rather than a state: running the migration job, and not
# updating any service until it has succeeded.

set -euo pipefail

ENVIRONMENT="${1:-}"
MODE="${2:---what-if}"
export IMAGE_TAG="${3:-$(git rev-parse --short HEAD)}"

if [[ "$ENVIRONMENT" != "staging" && "$ENVIRONMENT" != "prod" ]]; then
    echo "Usage: $0 <staging|prod> [--apply] [tag]" >&2
    exit 1
fi

: "${DB_PASSWORD:?set DB_PASSWORD}"
: "${SUPER_ADMIN_EMAIL:?set SUPER_ADMIN_EMAIL}"
# The read:packages token the services pull images with. Checked here rather
# than left to Bicep, because readEnvironmentVariable without a default fails
# partway through a subscription-scoped deployment, which is a worse place to
# find out than the first line of the script.
: "${GHCR_USERNAME:?set GHCR_USERNAME}"
: "${GHCR_TOKEN:?set GHCR_TOKEN (read:packages)}"
export DB_PASSWORD SUPER_ADMIN_EMAIL GHCR_USERNAME GHCR_TOKEN
export GHCR_REPOSITORY="${GHCR_REPOSITORY:-morgan-hacks/arctic}"
export SENTRY_DSN="${SENTRY_DSN:-}"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# A subscription-scoped deployment records the location it ran in and refuses
# to run again from another one. Changing region therefore means deleting the
# record first:
#
#   az deployment sub delete -n arctic-<env>-platform
#   az deployment sub delete -n arctic-<env>
LOCATION="${LOCATION:-centralus}"
GROUP="rg-mh-${ENVIRONMENT}"
JOB="caj-migrations-${ENVIRONMENT}"
PARAMS="${HERE}/${ENVIRONMENT}.bicepparam"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

if [[ "$MODE" != "--apply" ]]; then
    say "What-if — nothing will be changed"
    DEPLOY_APPS=true az deployment sub what-if \
        -l "$LOCATION" -f "$HERE/main.bicep" -p "$PARAMS" || true
    echo
    echo "Re-run with --apply to make these changes."
    exit 0
fi

say "1/5  Group and pull identity"
# Everything the later passes need to already exist. There is no registry to
# create any more -- the images live on ghcr.io -- but the identity still has to
# exist before the migration job references it, and the deployment name is kept
# as-is because renaming a subscription-scoped deployment orphans the old record
# rather than replacing it.
DEPLOY_PLATFORM=false DEPLOY_APPS=false az deployment sub create \
    -l "$LOCATION" -n "arctic-${ENVIRONMENT}-registry" \
    -f "$HERE/main.bicep" -p "$PARAMS" -o none

say "2/5  Images"
# Before migrations, because the job cannot run an image that is not there.
# Container Apps checks the image exists when the job is defined, not when it
# runs, so this has to happen before pass 3 rather than merely before pass 4.
#
# SKIP_PUSH=1 for a rollback: that tag already exists, and rebuilding it would
# produce different bytes from the ones being rolled back to.
if [[ "${SKIP_PUSH:-}" == "1" ]]; then
    echo "  skipped (SKIP_PUSH=1) — deploying tag ${IMAGE_TAG} as it already is"
else
    # Both sides read GHCR_REPOSITORY, exported above, so the path pushed to and
    # the path deployed from cannot disagree. They used to be two variables with
    # two defaults, and letting them differ pushed to one registry and deployed
    # from another -- which fails at pull time with nothing in the build log
    # explaining why.
    #
    # The push credential is deliberately not GHCR_TOKEN: that one is
    # read:packages, and pushing with it would mean one key that can do both.
    GHCR_PUSH_USER="${GHCR_PUSH_USER:-$GHCR_USERNAME}" \
    GHCR_PUSH_TOKEN="${GHCR_PUSH_TOKEN:?set GHCR_PUSH_TOKEN (write:packages)}" \
        "$HERE/push-images.sh" "$ENVIRONMENT" "$IMAGE_TAG"
fi

say "3/5  Platform  (Postgres, apps environment, migration job)"
# Apps deliberately excluded. Updating them now would put new code in front of
# a schema that has not been migrated yet.
DEPLOY_APPS=false az deployment sub create \
    -l "$LOCATION" -n "arctic-${ENVIRONMENT}-platform" \
    -f "$HERE/main.bicep" -p "$PARAMS" -o none

say "4/5  Migrations"
# Bounded, because an az command that hangs otherwise hangs until CI's own
# limit — which on a hosted runner is six hours of a job doing nothing.
timeout 300 az containerapp job start -g "$GROUP" -n "$JOB" -o none || {
    echo "  could not start the migration job" >&2
    exit 1
}
echo "  started; waiting"

# Wall clock, not iterations. The obvious version counts loops and calls it a
# ten-minute timeout, but each az call in the loop takes seconds of its own, so
# the real bound is whatever that adds up to — which is how a "ten minute"
# timeout ran for half an hour.
STATUS=""
DEADLINE=$(( $(date +%s) + 600 ))
while [ "$(date +%s)" -lt "$DEADLINE" ]; do
    STATUS="$(timeout 60 az containerapp job execution list -g "$GROUP" -n "$JOB" \
        --query "sort_by([], &properties.startTime)[-1].properties.status" -o tsv 2>/dev/null || echo "")"
    [[ "$STATUS" == "Succeeded" || "$STATUS" == "Failed" ]] && break
    sleep 10
done

if [[ "$STATUS" != "Succeeded" ]]; then
    # Fatal on purpose. Deploying anyway would put new code in front of a
    # schema it does not expect, which is worse than not deploying at all.
    echo "  migrations ${STATUS:-timed out} — services NOT deployed" >&2
    echo "  logs: az containerapp job logs show -g $GROUP -n $JOB --container migrations" >&2
    exit 1
fi
echo "  migrations succeeded"

say "5/5  Services"
# Platform excluded: it already exists, and re-writing the Postgres extension
# configuration while the server is still settling from the last write fails
# with ServerIsBusy. Each pass does one thing.
DEPLOY_PLATFORM=false DEPLOY_APPS=true az deployment sub create \
    -l "$LOCATION" -n "arctic-${ENVIRONMENT}" \
    -f "$HERE/main.bicep" -p "$PARAMS" -o none

FQDN="$(az deployment sub show -n "arctic-${ENVIRONMENT}" \
    --query properties.outputs.harborFqdn.value -o tsv)"

say "Deployed ${IMAGE_TAG}"
echo "  https://${FQDN}/api/health"
curl -sf "https://${FQDN}/api/health" && echo || echo "  (not answering yet — revisions take a moment)"
