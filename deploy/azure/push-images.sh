#!/usr/bin/env bash
#
# Builds the four images and pushes them to ghcr.io.
#
#   ./push-images.sh staging $(git rev-parse --short HEAD)
#
# Tagged by commit rather than by :latest, so every deployed thing can be
# traced back to what produced it. A rollback is then re-deploying a tag that
# already exists rather than rebuilding and hoping.

set -euo pipefail

ENVIRONMENT="${1:-}"
TAG="${2:-$(git rev-parse --short HEAD)}"
GHCR_REPOSITORY="${GHCR_REPOSITORY:-morgan-hacks/arctic}"

if [[ -z "$ENVIRONMENT" ]]; then
    echo "Usage: $0 <staging|prod> [tag]" >&2
    exit 1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$REPO_ROOT"

# Pushing needs a token, which the shared ACR did not: it had no admin user and
# the services pulled with a managed identity. ghcr.io has no equivalent, so
# there is a credential in this path now.
#
# In CI that credential is the job's own GITHUB_TOKEN, which expires when the
# job ends and is never stored anywhere. From a laptop it is a personal token
# with write:packages. Either way it is separate from the read:packages token
# the running services pull with -- pushing and pulling should not be the same
# key, and only one of them has to survive past the end of a job.
: "${GHCR_PUSH_USER:?set GHCR_PUSH_USER (in CI: github.actor)}"
: "${GHCR_PUSH_TOKEN:?set GHCR_PUSH_TOKEN (in CI: secrets.GITHUB_TOKEN)}"

printf '%s' "$GHCR_PUSH_TOKEN" | docker login ghcr.io -u "$GHCR_PUSH_USER" --password-stdin

build_and_push () {
    local name="$1" dockerfile="$2"
    local image="ghcr.io/${GHCR_REPOSITORY}/${name}:${TAG}"

    printf '\n\033[1m==> %s\033[0m\n' "$name"

    # linux/amd64 explicitly. Container Apps runs amd64, and a Mac builds
    # arm64 by default — an image that runs perfectly on the laptop that built
    # it and crash-loops in the cloud is a genuinely confusing hour.
    docker build --platform linux/amd64 -f "$dockerfile" -t "$image" .
    docker push "$image"
    echo "  pushed $image"
}

build_and_push atlas      src/atlas/Dockerfile
build_and_push harbor     src/harbor/Dockerfile
build_and_push lark       src/lark/Dockerfile
build_and_push migrations src/atlas/Dockerfile.migrations

printf '\n  All four pushed at tag %s\n' "$TAG"
