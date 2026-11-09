# Azure Container Apps

Bicep describes what should exist. Two small scripts do the parts that are
genuinely imperative: building images, and running the migration job.

```bash
az login                      # into the MorganHacks account, not a personal one

export DB_PASSWORD=$(cat ~/.mh-staging-db-password)
export SUPER_ADMIN_EMAIL=olola73@morgan.edu

./deploy/azure/deploy.sh staging              # what-if — changes nothing
./deploy/azure/deploy.sh staging --apply
```

`deploy.sh` builds and pushes the images itself, because the registry has to
exist before anything can be pushed to it. `SKIP_PUSH=1` skips that step, which
is what a rollback wants — the tag already exists and rebuilding it would
produce different bytes from the ones being rolled back to.

Optional, and each one is off when unset rather than half-configured:

```bash
export SENTRY_DSN=...                         # error reporting
export AWS_REGION=us-east-1                   # lark sends only when these are set
export AWS_ACCESS_KEY_ID=...
export AWS_SECRET_ACCESS_KEY=...
```

Resource groups included. `main.bicep` is subscription-scoped and owns them,
so an environment is one thing that either exists or does not rather than a
group somebody has to remember to create first.

Keep `DB_PASSWORD` somewhere real. Bicep never reads it back, so losing it
means resetting the admin password on the server.

## What's deployed today

| Resource group | Region | Holds |
|---|---|---|
| `rg-mh-shared` | `centralus` | the registry (`crmharctic`), and `id-mh-deploy` — the identity GitHub Actions authenticates as |
| `rg-mh-staging` | `centralus` | `cae-mh-staging`, `psql-mh-staging`, the three apps, `caj-migrations-staging`, `log-mh-staging` |
| `rg-mh-prod` | `eastus2` | the same shape, suffixed `-prod` |

Subscription `45944a03-563f-42c2-83f7-df274ae5ae1a`, Pay-As-You-Go, in the
tenant behind `morganhacks2022@gmail.com`. `naming.md` has the full naming
convention; the next section has why the regions differ.

## Two regions, one registry

`location` and `sharedLocation` are separate parameters on purpose. The
registry is shared across every environment and cannot move once created — a
resource cannot change region — while each environment's own resources can
live wherever that environment needs to be. Both `.bicepparam` files default
`sharedLocation` to `centralus`; only `location` differs between them, and
`naming.md` has the reasoning for why production's is `eastus2` and staging's
is not.

Conflating the two was a real failure, not a hypothetical one: it is why
production's first deploy broke at the registry stage with
`InvalidResourceLocation` the moment `location` was pointed at `eastus2`. See
[`docs/runbooks/first-production-deploy.md`](../../docs/runbooks/first-production-deploy.md)
for the full sequence.

`registryName` is a parameter too, read from `REGISTRY_NAME` and defaulting to
the shared `crmharctic`. There is only one real reason to override it: an
environment that cannot share that registry at all, because a managed
identity cannot be granted `AcrPull` on a registry in a different tenant or
subscription. That is a new registry to push images to, not just a new
parameter value.

## Why Bicep and not a shell script

A script has to be told *how* to reach the desired state, and gets idempotency
only where somebody hand-rolled it. Bicep describes the state, so re-deploying
converges — including correcting anything changed by hand in the portal.

The property worth the most is `what-if`. An infrastructure change can be
reviewed before it happens, the same way a code change is. That is the same
argument that rules out clicking through the portal, taken one step further:
a change to how production is provisioned should arrive as a diff.

## Layout

```
main.bicep              the front door — resource groups, then the modules
naming.md               naming convention and what every tag is for
modules/registry.bicep  the container registry, shared across environments
modules/platform.bicep  Postgres, resume storage, the apps environment, migrations
modules/apps.bicep      harbor, atlas, lark
staging.bicepparam      per-environment values
prod.bicepparam
deploy.sh               the sequence Bicep cannot express
push-images.sh
```

Values live in the `.bicepparam` files, which are type-checked against
`main.bicep` — a wrong or missing parameter fails at compile time rather than
half way through a deployment. Secrets are read from the environment with
`readEnvironmentVariable`, so these files are safe in the repository and there
is one fewer place a password gets committed by accident.

## Why it deploys in two passes

`deployApps` is false on the first pass. Migrations run between the two.

One pass would update the migration job and the services in the same
deployment, which puts new code in front of an old schema for however long the
job takes — and that window is where every migration bug lives. `deploy.sh`
runs platform, then migrations, then apps, and **stops if migrations fail**.
Not deploying beats deploying onto a schema the code does not expect.

This is the one thing Bicep cannot express: "run this and wait for it" is a
sequence, not a state.

## Shape of it

| | Ingress | Replicas |
|---|---|---|
| `harbor` | external — the only thing published | 0–3 |
| `atlas` | internal — harbor is the only path in | 0–3 |
| `lark` | none at all | 0–1 |
| `migrations` | a job | on demand |

Each replica's CPU and memory is `containerSize`, one of `0.25`–`2.0`, set by
`CONTAINER_SIZE` and defaulting to `0.5` (0.5 vCPU, 1 GiB). It is one choice
rather than two numbers because Container Apps only accepts the two in a fixed
1:2 ratio and does not say so until the deployment is already running.

The default is `0.5` because that is what these services have always run, and
for no stronger reason. It was introduced on the assumption that a smaller size
would lengthen cold starts badly; measuring both sizes against a confirmed-zero
replica count showed otherwise:

| size | cold | warm |
|---|---|---|
| 0.5 vCPU | 18.5s | 0.10–0.12s |
| 0.25 vCPU | 19.2s | 0.16s |

Six tenths of a second. The cold start is pulling a 95 MiB image and starting a
.NET runtime; neither waits on the half vCPU it didn't get. Working set is far
below either limit — 159–171 MiB for atlas and harbor, 272 MiB for lark — so
`0.25` is safe anywhere.

It also barely matters. Against a replica floor of zero, sleeping is the whole
saving: at the awake time these services accumulate, `0.25` costs nothing a
month and `0.5` about 65 cents. The default stays `0.5` only because changing
it would redeploy production to save eight dollars a year.

The direction worth using is upward — `0.75` or `1.0` for the weeks around an
open application window.

Minimum replicas is `warmReplicas` (atlas, harbor) or `larkWarmReplicas`
(lark), and both can be zero — but `lark` scaling to zero is only safe
together with the KEDA scale rule `apps.bicep` adds when
`LARK_WARM_REPLICAS=0`. Without that rule, nothing has any reason to wake a
worker that only ever polls a queue on a timer, and a queue with no worker is
one that silently stops sending while every dashboard reads green. Production
does not set either to zero; staging sets `WARM_REPLICAS=0` and cold-starts
atlas and harbor in about 22 seconds. See
[`docs/architecture/deployments.md`](../../docs/architecture/deployments.md#cost-production-stays-warm-staging-doesnt)
for the cost reasoning.

The registry lives in its own resource group so deleting an environment cannot
take the images with it — including the image a rollback needs.

**Noise you will see and can ignore:** every container app in both
environments logs `ScaledObjectCheckFailed: no triggers defined in the
ScaledObject` roughly every ten minutes, since 2026-09-12. Container Apps
creates a KEDA `ScaledObject` for every app, and one with no scale rule — which
is most of them, since only `lark` ever gets one — has no triggers to report
on. It is not evidence of anything breaking.

## Rolling back

```bash
./deploy/azure/deploy.sh staging --apply <older-tag>
```

Images are tagged by commit, never `:latest`, so this re-deploys bytes that
already exist rather than rebuilding and hoping. Note it re-runs migrations —
which is fine forward, and is why a migration that drops something needs a
second thought.

## Pulling images

Services pull with a **user-assigned managed identity** granted `AcrPull` on
the shared registry, one identity per environment. The registry has no admin
user.

The alternative was the registry's admin password: one static credential shared
by every service, readable by anyone with access to the resource, and rotating
it means redeploying everything at once. The identity is scoped to its
environment, grants exactly pull, and has nothing to leak.

Pushing still uses your own `az login`. Push belongs to a person or to CI, not
to a running service.

On a first deploy the role assignment occasionally has not propagated by the
time the apps start, and they fail to pull. Re-running `deploy.sh` fixes it —
the templates are idempotent and the grant is already there by then.

## Resumes

A storage account per environment, one private container, and a Storage Blob
Data Contributor grant on it for the same identity that pulls the images. Atlas
gets the account name and that identity's client id in configuration and
nothing else: **there is no access key anywhere**, because the account has
`allowSharedKeyAccess: false` and one does not work even if somebody finds it.

The build plan recommends Cloudflare R2. This is Azure Blob instead, and the
reasoning is short: we own this subscription, so the container is declared in
the same deployment as everything else with no second account, no second bill
and no credential to rotate. The portability the plan is protecting is that
`resume_key` holds a key rather than a URL, and that is bought in the code by
`IResumeStore` — moving to R2 later is a new implementation of one interface
and a copy of the objects, with no column to rewrite.

Nothing is ever public. Reads are user-delegation SAS links signed for five
minutes, with the content type and disposition inside the signature so a file a
stranger uploaded arrives as a PDF and nothing else.

## Not in the templates

**`Network__KnownNetworks`.** Container Apps terminates in front of harbor, so
until this names it, `RemoteIpAddress` is the platform's and every per-IP rate
limit shares one bucket for the whole internet. It needs the environment's
subnet, which does not exist until the environment does.
`docs/architecture/deployments.md` has the reasoning and how to check it.

**A private endpoint for Postgres.** The firewall rule allows Azure services,
which is now the weakest thing here: any Azure tenant's resources can reach the
server, though they still need the password. The fix is VNet integration with a
private endpoint, and it means recreating the Container Apps environment —
VNet cannot be added to an existing one. This was written as "worth doing
before production carries real applicant data"; production now does, so the
deadline this was waiting on has already passed. Not worth rebuilding staging
for on its own.

## On subscriptions

**Resolved:** this runs on a **Pay-As-You-Go** subscription
(`45944a03-563f-42c2-83f7-df274ae5ae1a`), in the tenant behind
`morganhacks2022@gmail.com` — not a Visual Studio subscription, whose monthly
credit is for development and testing only under its terms and would not have
been a licence to run registration on. That question used to be open; it no
longer is.

**Own these resources with the MorganHacks account, not a personal one.** Same
rule as the Vercel projects and `tech@morganhacks.com`: infrastructure tied to
somebody's student account is infrastructure that leaves when they graduate.

## Things the first real deploys found

Worth recording, because none of them show up in a test. The first four are
from staging's first deploy; the rest are from production's — which also hit
problems unique to being a second environment in a second region, and which
has its own page,
[`docs/runbooks/first-production-deploy.md`](../../docs/runbooks/first-production-deploy.md),
for the full sequence rather than the short version below.

**`eastus` is capacity-restricted on this subscription.** Postgres provisioning
is refused there outright, and the error is `Version should be in: []` rather
than anything about capacity. `centralus` is open and offers Postgres 18, which
is what docker-compose and the tests run — so local and deployed now match
exactly.

**`citext` has to be allow-listed on the server.** Azure refuses `CREATE
EXTENSION` regardless of the connecting user's privileges until the extension
is named in `azure.extensions`. Without it the notify schema cannot be created
at all.

**A subscription deployment records its location and will not move.** Changing
region means deleting the record first:
`az deployment sub delete -n arctic-<env>-platform`.

**Container Apps rejects a secret with an empty value.** "Off unless
configured" therefore has to mean the secret is absent, not blank — otherwise
the thing that lets this run with no accounts is the thing that stops it
deploying.

**Central US can refuse to create a new Container Apps environment outright
(`AKSCapacityHeavyUsage`).** Not a configuration mistake and not visible to
`what-if` — capacity is only evaluated at apply time. There is no setting that
fixes it, only a different region, which is part of why production ended up in
`eastus2`.

**Deleting a resource group deletes every role assignment scoped to it.** A
grant made directly on a resource group is only as durable as that resource
group is — recreating the group, for a region move or anything else, does not
bring the grant back with it. This is the real reason the deploy identity's
`User Access Administrator` grant moved to subscription scope, below.

## Deploys run in CI, not on a laptop

`.github/workflows/deploy-azure.yml` is what actually deploys.

| Trigger | Goes to |
|---|---|
| merge to `main` | staging, automatically |
| manual dispatch | staging, or production behind a required review |
| manual dispatch with a tag | an older tag — this is a rollback |

**Merging deploys staging, never production.** Staging mirrors main, which is
what makes it a rehearsal of what production will become. Promoting to
production is a decision somebody makes, not a side effect of merging a pull
request.

It watches `main` rather than the `staging` branch, and that is not an
oversight. The `staging` branch is fast-forwarded by `mirror-staging.yml` using
the default `GITHUB_TOKEN`, and GitHub deliberately does not fire `push`
workflows for pushes made with that token — its guard against a workflow
triggering itself forever. A deploy watching `staging` would therefore never
run at all, which is exactly what happened.

Running `deploy.sh` by hand still works and is the right tool when something is
broken. It is not how a normal deploy should happen: a deploy that depends on
one person's machine depends on that person being awake, having the tools, and
being logged into the right account, and it leaves no record of what shipped or
who shipped it.

### There is no Azure credential in GitHub

Authentication is OIDC. The workflow proves which repository and which
environment it is running as, and Azure trusts that exact pair through a
federated credential on `id-mh-deploy`. Nothing is stored that could leak,
because nothing is stored.

What the deploy identity may do:

| Grant | Scope | Why |
|---|---|---|
| Contributor | subscription | creates the resource groups and everything in them |
| User Access Administrator | subscription | grants AcrPull on the shared registry, and Storage Blob Data Contributor on each environment's resumes account |
| AcrPush | the registry | pushes images; Contributor does not cover data-plane push |

**`platform.bicep` assigns Storage Blob Data Contributor on the resumes
account, and Contributor cannot create role assignments** — without this
grant the platform pass fails with `AuthorizationFailed` on the assignment and
nothing else, which is also exactly what happens if the grant is missing on a
resource group that does not exist yet, i.e. a brand new environment.

This grant used to be made per resource group — `rg-mh-shared` for the
registry, then each environment's own group as it was created. It is at
subscription scope now, because the per-group version did not survive a
resource group being deleted and recreated: that happened during production's
first deploy, when a capacity problem forced `rg-mh-prod` to be rebuilt in a
different region, and the grant made on the old group vanished with it. A
subscription-scope grant has nothing to lose when any one resource group
goes away. Scoped this high, it is still the one permission worth being
stingy with — which is why it stops at *this* subscription and grants nothing
else.

### Configuration

Repository variables, because none of them are secrets: `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `SUPER_ADMIN_EMAIL`.

Environment secrets, set per environment: `DB_PASSWORD`, and later
`SENTRY_DSN`, `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`.

**Production's `DB_PASSWORD` was generated fresh for it, never copied from
staging.** Before production existed, this secret was deliberately left unset
on its GitHub environment, specifically so that creating a production
database password was a decision somebody made rather than something that
happened by copying staging's. The same rule applies to any environment added
after this one.
