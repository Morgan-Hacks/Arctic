/**
 * Light, dark, or whatever the machine says: the plumbing, with no React and
 * no DOM.
 *
 * Shared by all three frontends for the same reason reading-mode.ts is: three
 * copies of a cookie name and an attribute name drift, and a drift here is
 * invisible until somebody's console comes back light after they chose dark.
 *
 * The shape of the thing:
 *
 * - The **cookie** is the source of truth, and it is read on the server so the
 *   first byte already carries `data-theme`. A flash of a white page before a
 *   dark one is the single most noticeable bug this feature can have, and it is
 *   the one every naive implementation ships with, so nothing here waits for
 *   hydration when a cookie is present.
 * - The **operating system preference** decides when there is no cookie, and it
 *   decides *in CSS* rather than here. `prefers-color-scheme` is already wired
 *   through tokens.css, so the server's job for a reader who has never chosen
 *   is to write no attribute at all and let the stylesheet answer. That is why
 *   this file has no `prefersDark()` equivalent of `prefersReadingMode()`: the
 *   fallback needs no JavaScript, so it gets none.
 *
 * Three states rather than two, because "follow my machine" is a real answer
 * and the common one. A two-state toggle has to pick a side the first time it
 * is rendered, which means either ignoring the system preference or silently
 * converting it into an explicit choice the reader never made.
 */

/** The cookie. Same `arctic_` prefix as the sidebar's and reading mode's. */
export const THEME_COOKIE = "arctic_theme";

/** Set on `<html>`. Both palettes in tokens.css key off this. */
export const THEME_ATTRIBUTE = "data-theme";

/** Ten years, matching reading mode. Nobody wants to be re-asked about this. */
const COOKIE_MAX_AGE = 60 * 60 * 24 * 365 * 10;

/**
 * What a reader can choose.
 *
 * `system` is a choice, not the absence of one. Somebody who has been on dark
 * and moves back to `system` has decided something, and the control has to be
 * able to show that third position as selected rather than falling blank.
 */
export type Theme = "system" | "light" | "dark";

/** What ends up on `<html>`: an explicit palette, or nothing. */
export type ThemeAttribute = "light" | "dark" | undefined;

/**
 * What a cookie value means.
 *
 * Anything unrecognised is `undefined` rather than `system`, matching
 * parseReadingMode. The two behave identically on the page — both leave the
 * attribute off and let the media query decide — but they are not the same
 * fact, and a caller that wants to show the control's selected position needs
 * to know whether a reader chose `system` or has never been asked.
 */
export function parseTheme(raw: string | undefined | null): Theme | undefined {
  if (raw === "system") return "system";
  if (raw === "light") return "light";
  if (raw === "dark") return "dark";
  return undefined;
}

/**
 * The attribute for the first paint, from the cookie the server read.
 *
 * `undefined` means "write no attribute", which is the instruction for both a
 * reader who chose `system` and one who has never chosen. In both cases
 * `:root:not([data-theme="light"])` matches and `prefers-color-scheme` inside
 * it picks the palette — so the correct theme is painted on the first byte
 * without this file knowing what the machine prefers, which it cannot know.
 *
 * The inverse matters just as much: a reader who chose light gets
 * `data-theme="light"`, which is the one value that switches the media query
 * off. Light is not the absence of dark here; it is a thing you can ask for,
 * and asking for it has to survive a dark laptop.
 */
export function initialTheme(cookie: string | undefined | null): ThemeAttribute {
  const choice = parseTheme(cookie);
  return choice === "light" || choice === "dark" ? choice : undefined;
}

/**
 * The reader's stored choice, read from the browser.
 *
 * The client half, for the one thing the server cannot do: show the control
 * which of its three positions is selected when the page was served from a
 * cache that dropped the cookie. Kept separate from `initialTheme()` so the
 * server half stays pure and testable without a `document`.
 */
export function storedTheme(): Theme | undefined {
  if (typeof document === "undefined") return undefined;

  const found = document.cookie.split("; ").find((part) => part.startsWith(`${THEME_COOKIE}=`));
  const cookie = parseTheme(found?.slice(THEME_COOKIE.length + 1));
  if (cookie) return cookie;

  /*
   * The cookie first, then the mirror.
   *
   * The cookie wins because it is the copy the server reads, so trusting it
   * keeps the control's position and the painted palette telling the same
   * story. localStorage is only consulted when the cookie is gone — a browser
   * set to clear cookies on close, or a response served from a cache that
   * dropped it — which is the case the mirrored write in `rememberTheme()`
   * exists for. Writing a mirror and never reading it would make that write
   * decoration.
   *
   * In `try/catch` because Safari throws on `localStorage` in private
   * browsing, and the honest answer there is "no stored choice" rather than a
   * page that fails to render.
   */
  try {
    return parseTheme(localStorage.getItem(THEME_COOKIE));
  } catch {
    return undefined;
  }
}

/** The cookie string. `Path=/` so all three apps on a host share one choice. */
export function themeCookie(theme: Theme): string {
  return `${THEME_COOKIE}=${theme}; Path=/; Max-Age=${COOKIE_MAX_AGE}; SameSite=Lax`;
}

/**
 * Remember the choice.
 *
 * The cookie is what the server reads on the next navigation and is the half
 * that prevents the flash. localStorage is mirrored beside it because it
 * survives a browser configured to drop cookies on close, and reading it costs
 * nothing.
 *
 * Both writes are in `try/catch`. Safari throws on `localStorage` in private
 * browsing, and an exception here would take the page down for somebody who is
 * only trying to turn the lights off.
 */
export function rememberTheme(theme: Theme): void {
  try {
    document.cookie = themeCookie(theme);
  } catch {
    /* A browser that will not keep the cookie still gets the theme this session. */
  }

  try {
    localStorage.setItem(THEME_COOKIE, theme);
  } catch {
    /* Private browsing. The attribute below is what the reader actually sees. */
  }

  applyTheme(theme);
}

/**
 * Flip the attribute on `<html>`, which is what both palettes key off.
 *
 * `system` removes the attribute rather than working out what the machine
 * prefers and writing the answer. Removing it hands the question back to CSS,
 * which means a reader on `system` who changes their machine from light to
 * dark while the tab is open sees the console follow — no listener, no
 * re-render. Writing a resolved value here would freeze the page on whatever
 * the preference was at the moment they clicked.
 */
export function applyTheme(theme: Theme): void {
  if (theme === "system") {
    document.documentElement.removeAttribute(THEME_ATTRIBUTE);
  } else {
    document.documentElement.setAttribute(THEME_ATTRIBUTE, theme);
  }
}
