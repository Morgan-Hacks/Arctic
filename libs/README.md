# libs

Shared code, not deployed on its own. Five of these are real and in use;
three are scaffolding from the initial commit that nothing was ever built
into.

| | |
|---|---|
| [`audit`](audit/README.md) | Audit logging. The trail is written by database triggers; this is the read side and the thing that tells a transaction who is acting. |
| [`ui`](ui/README.md) | Shared design tokens and components, imported by relative path from all three Next.js apps — not published as a package. |
| `directory` | Shared directory choices and profile validation (`MorganHacks.Directory.Contracts`). A leaf library with no project references or stores, used by application forms. |
| `features` | Feature flags (`MorganHacks.Features`): one name per flag, read from `features.json` on every call, same string in every service and every frontend. |
| `observability` | Correlation IDs, structured logging, Sentry redaction (`MorganHacks.Observability`). Used by every .NET service. |

`contracts`, `data`, and `forms` have held nothing but a README since the
project was scaffolded — no code, no project file, nothing referencing them.
What each was meant to hold ended up somewhere else instead: request and
response shapes are defined per-service rather than in a shared contracts
library; data access is per-module (`PostgresApplicantStore` and its
neighbors in `MorganHacks.Applications/Data`, for instance); and the actual
form-rendering and validation logic lives in
`src/atlas/MorganHacks.Applications/Forms`. If one of these three ever gets
built out for real, that is the code it would be carved out of.
