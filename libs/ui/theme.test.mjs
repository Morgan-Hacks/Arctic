import assert from "node:assert/strict";
import { test } from "node:test";
import {
  initialTheme,
  parseTheme,
  storedTheme,
  themeCookie,
  THEME_ATTRIBUTE,
  THEME_COOKIE,
} from "./theme.ts";

/*
 * docs/feature-flags.md is explicit that a test covering only the off case
 * passes just as well when the route is broken, so every position is here.
 *
 * These are the pure half: what a cookie value means, and what the server
 * concludes from it. The half that touches the DOM — applyTheme,
 * rememberTheme — needs a browser, and pretending otherwise in a test runner
 * would test the stubs rather than the code. storedTheme is the exception: its
 * no-document branch is reachable here and worth pinning, because that is the
 * branch the server takes.
 */

test("a stored choice is honoured exactly as written", () => {
  assert.equal(parseTheme("system"), "system");
  assert.equal(parseTheme("light"), "light");
  assert.equal(parseTheme("dark"), "dark");
});

test("an absent or malformed cookie is no choice rather than a theme", () => {
  // Same reasoning as parseReadingMode: reading a typo as a real answer would
  // overrule somebody rather than fall back to asking their machine.
  assert.equal(parseTheme(undefined), undefined);
  assert.equal(parseTheme(null), undefined);
  assert.equal(parseTheme(""), undefined);
  assert.equal(parseTheme("Dark"), undefined);
  assert.equal(parseTheme("DARK"), undefined);
  assert.equal(parseTheme("auto"), undefined);
  assert.equal(parseTheme("true"), undefined);
  assert.equal(parseTheme("1"), undefined);
});

test("an explicit palette survives to the first paint", () => {
  // The whole point of reading the cookie on the server: no flash, in either
  // direction.
  assert.equal(initialTheme("light"), "light");
  assert.equal(initialTheme("dark"), "dark");
});

test("light is a choice, not the absence of dark", () => {
  // The case that breaks if somebody "simplifies" this to a boolean. Asking
  // for light has to write the attribute, because `data-theme="light"` is the
  // one value that switches the prefers-color-scheme block off — otherwise a
  // reader who chose light on a dark laptop gets a dark page.
  assert.equal(initialTheme("light"), "light");
  assert.notEqual(initialTheme("light"), undefined);
});

test("following the system writes no attribute at all", () => {
  // Both the explicit "Auto" and the reader who has never been asked. CSS
  // answers this one, so the server's job is to stay out of the way: anything
  // other than undefined here would pin a palette and defeat the media query.
  assert.equal(initialTheme("system"), undefined);
  assert.equal(initialTheme(undefined), undefined);
  assert.equal(initialTheme(null), undefined);
  assert.equal(initialTheme("nonsense"), undefined);
});

test("system and never-asked are the same paint and different facts", () => {
  // They render identically and the control has to tell them apart, which is
  // why parseTheme keeps the distinction initialTheme throws away.
  assert.equal(initialTheme("system"), initialTheme(undefined));
  assert.equal(parseTheme("system"), "system");
  assert.equal(parseTheme(undefined), undefined);
});

test("the cookie is scoped to the host, not to one app", () => {
  // Path=/ is what lets the console, the portal and the forms app agree. All
  // three are served from one hostname, and a per-app path would mean choosing
  // dark three times.
  for (const theme of ["system", "light", "dark"]) {
    const cookie = themeCookie(theme);
    assert.match(cookie, new RegExp(`^${THEME_COOKIE}=${theme};`));
    assert.match(cookie, /Path=\/;/);
    assert.match(cookie, /SameSite=Lax/);
  }
});

test("the cookie outlives the event", () => {
  // Ten years, matching reading mode. A preference nobody re-asks about.
  const [, seconds] = themeCookie("dark").match(/Max-Age=(\d+)/);
  assert.ok(Number(seconds) > 60 * 60 * 24 * 365 * 5, `Max-Age was ${seconds}`);
});

test("a cookie value round-trips through the string the browser stores", () => {
  // The two halves have to agree about the format, or the control reads back
  // nothing and silently resets to Auto on the next navigation.
  for (const theme of ["system", "light", "dark"]) {
    const [pair] = themeCookie(theme).split("; ");
    const value = pair.slice(THEME_COOKIE.length + 1);
    assert.equal(parseTheme(value), theme);
  }
});

test("reading the stored choice on the server is a no, not a crash", () => {
  // This module is imported by three root layouts, which run where there is no
  // document. Throwing here would take down every page in all three apps.
  assert.equal(typeof document, "undefined");
  assert.equal(storedTheme(), undefined);
});

test("the attribute is the one both palettes key off", () => {
  // Named once and shared, so a rename cannot leave tokens.css keyed off an
  // attribute nothing writes any more.
  assert.equal(THEME_ATTRIBUTE, "data-theme");
  assert.equal(THEME_COOKIE, "arctic_theme");
});
