# Local hacker directory fixtures

From the repository root, start the existing local Postgres and Azurite stack and apply migrations, then run:

```sh
./deploy/local/seed-hackers.sh --directory
./deploy/local/seed-hackers.sh --directory --apply
```

The first command is a preview. The second verifies the local Docker Postgres cluster before writing. `--directory --staging` is refused. The separate `mock-directory-v1` event has a past directory opening date; no real event's date is changed.

Fixtures create 41 suppressed-email hacker identities, 40 confirmed applications, 30 opted-in profiles before moderation/opt-out adjustments, and 10 generated placeholder photos in local Azurite. Existing profiles and application decisions are preserved on reruns. A failed partial seed may require inspecting the incomplete fixture before retrying photos; it is not a production repair tool.

Use the existing local development sign-in flow with `mock-directory-01@example.com` through `mock-directory-41@example.com`. Number 41 is accepted but has not RSVP'd. Numbers 28 and 29 are hidden; 30 is opted out. Never send email to these fixture addresses.

Enable `ENABLE_HACKER_PORTAL_FEATURE=true` and `ENABLE_DIRECTORY_FEATURE=true` on the local API and portal; the console also needs `ENABLE_DIRECTORY_FEATURE=true`. Defaults remain off. Production/staging opening dates must be chosen separately by an organizer.

Teams contain at most four hackers, including the profile owner. “Teammates needed” allows one, two, or three.

The seed executable directly references ImageSharp to generate placeholder photos. Debug builds run without a key; Release builds require an appropriate Six Labors key covering `MorganHacks.Seed`, in addition to `MorganHacks.Api` for uploads. Do not put license keys in Git.
