/**
 * Whether a feature is on.
 *
 * Server-side only. It reads process.env by a name computed at runtime, which
 * works in a server component or a route handler and does not work in the
 * browser: Next replaces `process.env.SOMETHING` in client bundles by matching
 * the literal text, so a computed lookup finds an empty object there and every
 * flag would read as its file default no matter what the environment says.
 * Importing this into a "use client" file is the one way to get that wrong.
 *
 * features.json holds the default, and an environment variable of the same name
 * upper-cased overrides it -- the same arrangement the .NET services use, with
 * the same key names, so a flag can be found by searching for one string across
 * the whole repository.
 */
import base from "@/features.json";
import staging from "@/features.staging.json";
import { environmentName, flagValue, resolveDefaults } from "../../../libs/features/flags";

/**
 * The flag files for this app, with the environment's overlay laid over the base.
 *
 * Resolved once at module load rather than per call: the answer cannot change
 * within a process, since it depends only on which environment this is.
 *
 * The overlay is imported by name rather than looked up by filename, because a
 * static import is the only kind Next can follow -- a computed
 * `import(`features.${env}.json`)` is not statically analysable and would not be
 * bundled. One import per environment that has an overlay, which today is
 * staging alone. Adding another means adding a line here, and that visibility
 * is worth more than the cleverness it costs.
 */
const OVERLAYS: Record<string, Record<string, boolean | undefined>> = { staging };
const defaults = resolveDefaults(base, OVERLAYS[environmentName(process.env) ?? ""]);

/** The applicant portal. Off means /portal sends people to the public site. */
export const HACKER_PORTAL = "enable_hacker_portal_feature";

/**
 * Reading mode: OpenDyslexic plus the spacing that makes it work.
 *
 * Off means the toggle does not render anywhere and no element carries
 * `data-reading`, so the stylesheet in libs/ui/reading-mode.css matches nothing.
 * The rule that a feature which is off should be indistinguishable from one
 * that was never built is what decides that; see docs/feature-flags.md.
 */
export const READING_MODE = "enable_reading_mode_feature";

/**
 * The 2027 Heist look for the portal: navy and orange, code rain, the vault.
 *
 * Off means the frame carries no `heist` class, none of the Heist components
 * render and app/portal/heist.css matches nothing, so the portal is exactly the
 * calm one it was before.
 */
export const HEIST_THEME = "enable_heist_theme_feature";

/**
 * Dark mode, and the control that chooses it.
 *
 * Off pins `data-theme="light"` exactly as the three layouts did before this
 * existed and renders no control, so a console with the flag off is
 * indistinguishable from one that never had the feature -- see
 * docs/feature-flags.md for why that is the bar.
 *
 * On lets the cookie decide, and `prefers-color-scheme` decide for anybody who
 * has not chosen. It is a flag rather than a straight change because the two
 * apps are one product to the people using them: the layouts used to say that
 * an applicant on a dark laptop and an organizer on a light console were
 * seeing two different things, and turning this on for one app and not the
 * other brings that back. Flip all three together.
 */
export const DARK_MODE = "enable_dark_mode_feature";

export function isOn(flag: string): boolean {
  // The variable sits above both files, which is what makes it the lever
  // somebody can throw during an incident without rebuilding an image. The
  // comparison to the literal "true", and the treatment of an unknown flag as
  // off, both live in libs/features/flags.ts so the rule is written once.
  return flagValue(process.env[flag.toUpperCase()], defaults[flag]);
}
