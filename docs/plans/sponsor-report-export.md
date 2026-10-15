# Sponsor report export

Plan for issue [#206](https://github.com/Morgan-Hacks/Arctic/issues/206).

This is a design before any code. There is no sponsor table, no sponsor
endpoint, and no data being collected for sponsors today. `sponsors.view`,
`sponsors.edit` and `sponsors.view_financials` are grants waiting for
something to guard. The work below is what they end up guarding.

## Goal

Give a sponsor the aggregate numbers their agreement buys — attendance,
demographic rollups, and whatever MorganHacks committed to in that tier — as
a downloadable report, without exposing individual applicants. Give finance
the money view separately, and never let one download contain both halves.

## What already exists

- `Permission.SponsorsView`, `SponsorsEdit`, `SponsorsViewFinancials`
  in `src/atlas/MorganHacks.Identity/Domain/Permission.cs`.
- Grants in `0050_teams_as_they_are.sql`: the `sponsorship` team is not
  mentioned there — finance holds `sponsors.view` and
  `sponsors.view_financials`; the `sponsorship` team is a phase-two
  placeholder and needs its own grants once it exists.
- `sponsors.view_financials` is on `Permission.Sensitive`, so it carries
  the confirmation-and-notification treatment.
- Applicant-side aggregates already exist:
  `PostgresApplicantAnalyticsStore` (`IApplicantAnalyticsStore`),
  status/school/started/submitted counts, and an opt-in demographic
  breakdown (gender, education, first-time-vs-returning).
- `mlh_data_sharing_at` records consent to share with MLH specifically.
  It is not a general sponsor-consent flag and must not be treated as one.

## Non-goals

- No individual applicant data leaves the system for a sponsor. No names,
  emails, schools as identifiers, or files. Aggregates only.
- No résumé sharing in this plan. Consent for that is a separate, larger
  conversation; `mlh_data_sharing_at` is not that consent.
- No sponsor-facing portal. Sponsors do not log in; exports are pulled by
  MorganHacks staff with the sponsor permission and sent out manually.
- No billing or invoicing. Finance tracks money committed in this system;
  payment processing stays outside it.
- The applicant numbers a sponsor sees are counts, never lists.

## The line to hold

Two audiences, two downloads:

1. **Numbers** — attendance, demographics, what was promised in the tier.
   Behind `sponsors.view`. Safe to give to a sponsor.
2. **Money** — what a sponsor paid, payment status, invoice references.
   Behind `sponsors.view_financials`. Finance only.

A single endpoint that returns both is a defect, and a test should assert
that no response carries a financial field on the numbers route and no
applicant aggregate on the money route. Sensitive-list treatment applies to
the money route because that is where PII-adjacent exposure actually
happens (a leaked sponsorship spreadsheet identifies a sponsor's budget).
The numbers route is not sensitive, because aggregates are not personal data.

## Data model

New migration `0057_sponsors.sql` (next free after `0056_check_in_overrides`).

### `sponsors.sponsors` — who the money is from

| column | type | notes |
|---|---|---|
| `id` | uuid pk | |
| `event_id` | uuid fk `events.events` | scoped like inventory |
| `name` | text not null | legal/trading name shown on reports |
| `tier` | text not null | slugs: `presenting`, `gold`, `silver`, `bronze`, `community` |
| `primary_contact_name` | text | nullable |
| `primary_contact_email` | text | nullable; check format like respondents |
| `created_at`, `updated_at` | timestamptz | |

### `sponsors.agreements` — what was promised and for how much

One sponsor can have several agreements across events/years, and a re-signed
sponsor keeps its history.

<|end|> column | type | notes |
|---|---|---|
| `id` | uuid pk | |
| `sponsor_id` | uuid fk `sponsors.sponsors` | |
| `event_id` | uuid fk `events.events` | denormalised for scoping |
| `tier` | text not null | frozen at signing time, even if the tier definition changes |
| `amount_cents` | bigint | money, nullable for in-kind |
| `currency` | text | default `USD` |
| `committed_at` | timestamptz not null | |
| `notes` | text | internal only; never appears on the sponsor-facing report |

### `sponsors.tiers` — what a tier actually buys

A small lookup so the report knows which aggregates to include. This is the
answer to "the export is shaped by the sponsorship tier, not by what is easy
to query."

| column | type | notes |
|---|---|---|
| `slug` | text pk | |
| `name` | text not null | |
| `promises` | jsonb not null | e.g. `{"headcount":true,"schools":true,"majors":true,"first_time_rate":true}` |

Keep promises as an explicit allow-list rather than deriving from tier name.
A tier can be customised per sponsor without a migration.

### `sponsors.payments` — finance only

| column | type | notes |
|---|---|---|
| `id` | uuid pk | |
| `agreement_id` | uuid fk `sponsors.agreements` | |
| `amount_cents` | bigint not null | |
| `received_at` | timestamptz | null until received |
| `method` | text | `wire`, `card`, `in_kind` |
| `reference` | text | invoice/PO number |
| `recorded_by` | uuid | nullable on delete, like movements |

`payments` is only ever read through the financials route. It is not joined
into any aggregate endpoint, not even as a count.

## Permissions and routes

Permission stays the unit. `Permission.Sensitive` already contains
`sponsors.view_financials`; nothing new is sensitive on the numbers side.

New atlas module `SponsorEndpoints.cs`, registered in `Program.cs` next to
`MapInventory()`.

| route | permission | what |
|---|---|---|
| `GET /admin/sponsors` | `sponsors.view` | list sponsors for the current event, tier and agreement summary, no money |
| `GET /admin/sponsors/{id}` | `sponsors.view` | one sponsor's agreement, tier, promised metrics |
| `POST /admin/sponsors` | `sponsors.edit` | create sponsor + agreement |
| `PATCH /admin/sponsors/{id}` | `sponsors.edit` | edit contact/name/tier |
| `GET /admin/sponsors/{id}/report.csv` | `sponsors.view` | aggregate report shaped by the tier's `promises` |
| `GET /admin/sponsors/{id}/financials.csv` | `sponsors.view_financials` | money only, no applicant data |

The two CSV routes are separate resources, not a `?include=financials`
flag, so the boundary is visible in the URL and in the route's permission
attribute — the same reasoning as `responses.csv` next to `responses`.

## Report shape

`report.csv` is built from a single aggregate query per promised metric, so
no applicant row is ever serialised. Example columns, gated by `promises`:

- always: `sponsor`, `tier`, `event`, `report_generated_at`, `total_applicants`,
  `total_submitted`, `acceptance_rate`
- `schools`: `school`, `submitted_count` (top N + "other")
- `majors`: `level_of_study`, `submitted_count`
- `gender`: `gender`, `submitted_count` (aggregate only, suppressed when a
  bucket is below 5 to avoid re-identification)
- `first_time_rate`: `first_time_hackers`, `returning_hackers`,
  `not_provided`
- `check_in`: `checked_in_count`, `checked_in_rate`

The k-anonymity floor (5) matters more than the rest of this plan. A report
with a bucket of one is a de-anonymised applicant, and a sponsor who was
promised "demographics in aggregate" asked for statistics, not people.

`financials.csv` columns: `sponsor`, `tier`, `agreement_id`, `amount`,
`currency`, `committed_at`, `received_at`, `method`, `reference`. It
deliberately has no event-scoped applicant counts.

## Console (portaladmin)

- New `/sponsors` section, gated on `sponsors.view` (numbers) and showing
  the financials tab only with `sponsors.view_financials`.
- Generate report button downloads `report.csv`; finance-only tab downloads
  `financials.csv`.
- Edit behind `sponsors.edit`.
- Section added to `sections.ts`, which still treats `needs` as any-of.

## Audit

- `sponsors.created`, `sponsors.edited` written through `logActivity`, like
  inventory, because money changes need a trail.
- Downloading a report is itself logged with sponsor id and actor — it is
  the moment data leaves the system, same as `applications.export`.
- Financials downloads get their own audit action
  (`sponsors.financials_exported`) so the trail distinguishes audience.

## Tests

Following the pattern of `CheckInOverrideTests.cs` and `InventoryImageTests.cs`:

- permission gate on every route (403 without the right permission, and
  specifically `sponsors.view` cannot read `financials.csv`).
- report CSV columns appear and disappear with the tier's `promises`.
- k-anonymity floor suppresses small buckets.
- financials CSV has none of the applicant columns.
- `sponsors.view_financials` on `Sensitive` shows in
  `EffectivePermissionsTests` / grant screens.
- migration applies on a clean database (`DatabaseInvariantTests`).

## Build order

1. Migration + Drizzle/Entity-less Dapper store (`PostgresSponsorStore`,
   `PostgresSponsorReportStore`).
2. Backend endpoints + permission gates + audit.
3. `report.csv` aggregate query and k-anonymity floor.
4. `financials.csv`.
5. Console list/detail/edit and both downloads.
6. Tests above.
7. Seed the `sponsorship` team with `sponsors.view` and `sponsors.edit`
   (not financials) via a migration, and add finance's existing grant to
   a second migration rather than editing `0050`.

## Open decisions

- Whether a sponsor agreement spans one event or a year. The schema above
  assumes per-event, matching `applications.applications.event_id`. A
  year-level sponsor would need either a synthetic event or a nullable
  `event_id`; decide before the first migration ships.
- Whether tier definitions are config or data. The plan proposes data
  (`sponsors.tiers`) so a tier can be customised without a deploy, but that
  adds a screen to maintain. A static enum in code is the cheaper option if
  nobody expects to change tiers at runtime.
- Whether the "presenter" tier (full name on marketing) is a report column
  or a separate, manual artefact. Keep it out of the automated export until
  the first sponsor with that tier asks for it.
- Whether k-anonymity floor is 5 or 10. Smaller event, larger floor.
- Whether downloads are CSV only or also XLSX. CSV is the lower-common-
  denominator and matches the existing `responses.csv`. Revisit when a
  sponsor asks for something Excel-friendly.
## Vocabulary (from the issue)
- "numbers" and "money" are the two halves.
- MLH consent is not sponsor consent.
- "phase-two placeholders" — the permissions exist before the data does.
