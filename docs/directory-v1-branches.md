# Directory V1 branch stack

These ten local feature branches start from staging commit `5fbf20d476f3d6f142f641741fd6858b4297bc0a` (also main at preparation time). Each branch contains one new feature commit on top of the preceding branch. They are a dependency stack, not ten independent changes based on staging.

| Order | Feature branch | Review against |
|---|---|---|
| 1 | `feat/directory-application-fields` | `staging` |
| 2 | `feat/directory-module` | `feat/directory-application-fields` |
| 3 | `feat/directory-rsvp-optin` | `feat/directory-module` |
| 4 | `feat/directory-profile-api` | `feat/directory-rsvp-optin` |
| 5 | `feat/directory-photos` | `feat/directory-profile-api` |
| 6 | `feat/directory-browse-api` | `feat/directory-photos` |
| 7 | `chore/directory-seed-data` | `feat/directory-browse-api` |
| 8 | `feat/portal-directory-profile` | `chore/directory-seed-data` |
| 9 | `feat/portal-directory-browse` | `feat/portal-directory-profile` |
| 10 | `feat/console-directory-moderation` | `feat/portal-directory-browse` |

## Shipping order

1. Push and review `feat/directory-application-fields` against `staging`. It adds optional application questions and the form-builder upgrade action. Existing published forms are not silently republished.
2. Review each later branch against its preceding feature branch to see just that feature. Merge into staging in the listed order. Once the predecessor is merged, retarget the next PR to staging.
3. Preserve commit ancestry with merge commits. If your repository requires squash merges, rebase the remaining stack onto the updated staging branch before proceeding; otherwise later PRs can show already-merged features again.
4. Keep `ENABLE_DIRECTORY_FEATURE` off while integrating unfinished features. The default remains false in the API and both portals. Portal access separately requires `ENABLE_HACKER_PORTAL_FEATURE`.
5. Do not enable browsing until organizers choose an opening date. A missing opening date keeps browsing closed. Maximum team size is four, so teammates needed ranges from one to three.

Example (run from any checkout of Arctic):

```sh
git push -u origin feat/directory-application-fields
```

Push later branch names as their reviews are ready. No branch was pushed and no staging deployment was run as part of preparing this stack.

## Release blocker begins at feature 5

The first four branches do not reference ImageSharp and were checked with Release builds. `feat/directory-photos` introduces ImageSharp 4.1.3. That branch and its descendants require an appropriate Six Labors key for Release builds. Feature 7 also uses ImageSharp in `MorganHacks.Seed` for generated photos.

Supply `SIXLABORS_LICENSE_KEY` as a GitHub repository secret for PR/nightly builds and in the applicable deployment environment if its secrets override repository configuration. Build processes receive it as `SixLaborsLicenseKey`; Docker receives it through a BuildKit secret. The key must cover both `MorganHacks.Api` and `MorganHacks.Seed` when assembly-scoped. Do not commit the key, and do not bypass the Release license check.

The deployment directory variable is `ENABLE_DIRECTORY_FEATURE` (matching the runtime). Set the same variable separately on the Vercel portal projects when enabling the feature; Azure configuration does not configure Vercel.

## Integration details

- Application questions and shared validation live in the contracts/application modules. Optional contact fields are normalized and remain optional.
- Module persistence is shared infrastructure; public own-profile, photo, browse and organizer APIs are introduced in their respective branches.
- Directory tests are split by feature and introduced alongside each feature.
- Classification choices and the four-person limit are included in shared validation. The profile UI branch includes the classification dropdown and optional editable teammate suggestions.
- Staging already contains migrations through 0053, so the directory migrations are 0054 and 0055 in this stack. The second expands the teammate constraint to three. Use these branch migrations for staging, not the older uncommitted draft copies from the original checkout.
- The original uncommitted state is preserved in the Git stash named `directory-v1-before-feature-branches-2026-10-14`. The completed stack is checked out on `feat/console-directory-moderation` in the normal repository directory. The sample browser preview remains separate and is not a production data source.
- See [local fixture instructions](directory-local-fixtures.md) after merging the seed branch.

## Remaining launch work

These branches organize the current implementation; they do not imply production readiness. The license, opening date, visual polish and a complete staging apply → accept → RSVP → upload → browse smoke test remain. Photo storage uses the existing private Azure account/container configuration.

## Validation performed

- Every branch: backend solution build and the directory/form/RSVP regression selection.
- Features 1–4: Release builds passed without an ImageSharp dependency.
- Features 5–10: Debug builds passed; Release remains blocked on the pending key.
- Complete stack: 165 API regression tests passed, including 49 directory cases.
- Existing seed utility: 18 tests passed.
- All three frontends: production builds passed using dependencies installed from current staging lockfiles.
- Browser sample preview is not an end-to-end staging deployment test.

- Feature 1: `0765994` — Release; 141 passing tests.
- Feature 2: `879854c` — Release; 143 passing tests.
- Feature 3: `f2df088` — Release; 147 passing tests.
- Feature 4: `32c1369` — Release; 153 passing tests.
- Feature 5: `a47493a` — Debug; 162 passing tests.
- Feature 6: `4970861` — Debug; 164 passing tests.
- Feature 7: `7986e6d` — Debug; 164 passing tests.
- Feature 8: `e335217` — Debug; 164 passing tests.
- Feature 9: `5bd8574` — Debug; 164 passing tests.
- Feature 10: organizer moderation and this handoff guide; Debug, 165 passing tests.
