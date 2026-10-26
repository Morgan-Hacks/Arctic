/**
 * Which environment this is, and what the flag files say for it.
 *
 * The TypeScript half of libs/features. The C# half is MorganHacks.Features,
 * and the two resolve flags the same way on purpose: a base file, an optional
 * per-environment overlay on top of it, and an environment variable above both.
 * One flag name, three layers, same order in all six services.
 *
 * This file holds the part with no imports and no `process`, so it can be
 * tested without a Next build and without pretending to be a browser. Each app
 * composes it with its own JSON imports in lib/features.ts, the same way the
 * three reading-mode and theme toggles share libs/ui and keep only a thin
 * shell apiece.
 *
 * ------------------------------------------------------------ why overlays ---
 *
 * features.json is committed, so it has one value per *branch*. main is
 * mirrored onto staging and also drives the production deploy, which means the
 * base file alone cannot say "on in staging, off in production" -- the two
 * environments are built from the same tree. The only per-environment lever was
 * an environment variable set in a dashboard, which works and is invisible:
 * nothing in the repository tells you what is on in production, and a flag left
 * on is exactly the thing nobody notices.
 *
 * An overlay file puts that answer back in git, where it is a reviewable diff.
 * The environment variable stays on top, because it is the lever somebody
 * throws during an incident and it has to beat a file baked into an image.
 */

/** What a flag file looks like once parsed. Unknown keys are allowed. */
export type FlagFile = Record<string, boolean | undefined>;

/**
 * The environment's name, lower-cased, or `undefined` for "no overlay".
 *
 * Two sources, in order:
 *
 * - `APP_ENV`, when somebody has said so explicitly. This is the escape hatch:
 *   it works locally (`APP_ENV=staging npm run dev`), and it works if staging
 *   ever stops being a branch.
 * - `VERCEL_GIT_COMMIT_REF`, which Vercel sets by itself at build and at
 *   request time. staging *is* a branch here -- mirror-staging.yml force-pushes
 *   main onto it -- so the branch name already is the environment name and
 *   nothing has to be configured by hand for this to work.
 *
 * Anything else is `undefined`, which means no overlay and the base file
 * stands. That is deliberately the answer for production and for every preview
 * branch: an overlay has to be opted into by name, so a new branch cannot
 * inherit staging's flags by accident.
 *
 * Lower-cased on the way out so the filename is the same string in every
 * service. .NET would hand us `Staging` and Vercel hands us `staging`, and a
 * repository with both features.Staging.json and features.staging.json in it
 * is a repository where somebody edits the wrong one.
 */
export function environmentName(env: Record<string, string | undefined>): string | undefined {
  const explicit = env.APP_ENV?.trim();
  if (explicit) return explicit.toLowerCase();

  const branch = env.VERCEL_GIT_COMMIT_REF?.trim();
  if (branch === "staging") return "staging";

  return undefined;
}

/**
 * The base file with the overlay laid over it.
 *
 * A shallow merge, and shallow is the whole design: an overlay names only the
 * flags that differ, so reading one tells you what is different about that
 * environment rather than restating everything. A key absent from the overlay
 * keeps the base value; it does not become false.
 *
 * `overlay` is optional because most services will not have one. Passing
 * `undefined` returns the base unchanged rather than throwing, since "there is
 * no overlay for this environment" is the normal case, not an error.
 */
export function resolveDefaults(base: FlagFile, overlay?: FlagFile): FlagFile {
  if (!overlay) return { ...base };
  return { ...base, ...overlay };
}

/**
 * One flag's answer.
 *
 * `override` is the raw environment variable, which is a string or absent.
 * Compared to the literal "true" rather than treated as truthy: an unset
 * variable is `undefined`, but a variable set to the string "false" is
 * non-empty and truthy, and would otherwise turn a feature on while appearing
 * to turn it off.
 *
 * An unknown flag is off. A typo in a flag name should hide a feature rather
 * than expose one, because the first is noticed and the second is not.
 */
export function flagValue(override: string | undefined, fallback: boolean | undefined): boolean {
  if (override !== undefined) return override === "true";
  return fallback ?? false;
}
