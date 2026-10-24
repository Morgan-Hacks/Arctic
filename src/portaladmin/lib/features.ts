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
import defaults from "@/features.json";

/** The applicant portal. Off means /portal sends people to the public site. */
export const HACKER_PORTAL = "enable_hacker_portal_feature";

/**
 * The check-in desk. Off hides it from the nav and refuses direct visits.
 * Overridden with ENABLE_CHECK_IN_DESK=true; still needs checkin.scan to scan.
 */
export const CHECK_IN_DESK = "enable_check_in_desk";

/**
 * Reading mode: OpenDyslexic plus the spacing that makes it work.
 *
 * Off means the toggle does not render and nothing carries `data-reading`. See
 * docs/feature-flags.md for why a feature which is off must be
 * indistinguishable from one that was never built.
 */
export const READING_MODE = "enable_reading_mode_feature";

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
  // Explicitly compared to "true" rather than treated as truthy. An unset
  // variable is undefined, but a variable set to "false" is a non-empty string
  // and would otherwise turn the feature on while appearing to turn it off.
  const override = process.env[flag.toUpperCase()];
  if (override !== undefined) {
    return override === "true";
  }

  const fallback = (defaults as Record<string, boolean | undefined>)[flag];

  // An unknown flag is off. A typo in a flag name should hide a feature rather
  // than expose one, since the first is noticed and the second is not.
  return fallback ?? false;
}
