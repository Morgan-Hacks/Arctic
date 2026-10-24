"use client";

/**
 * The theme control.
 *
 * COPY: needs sign-off on every string below, per docs/design-brief.md 2.5.
 * The wording is a proposal, not a decision.
 *
 * The component is repeated per app rather than shared from libs/ui, exactly
 * as reading-toggle.tsx and error-toast.tsx are: React's types do not resolve
 * from that directory, so a shared .tsx there fails typecheck in all three
 * apps. The logic and the styles are shared (libs/ui/theme.ts and
 * theme.module.css) so the three copies cannot drift in behaviour — only this
 * thin render shell repeats.
 */
import { useEffect, useState } from "react";
import { applyTheme, rememberTheme, storedTheme, type Theme } from "../../../libs/ui/theme";
import styles from "../../../libs/ui/theme.module.css";

/**
 * The three positions.
 *
 * `short` is what fits in a 280px sidebar; `full` is what a screen reader
 * says. "Auto" alone is not a sentence somebody can act on, and the long form
 * costs nothing because it is never painted.
 */
const OPTIONS: ReadonlyArray<{ value: Theme; short: string; full: string }> = [
  // COPY: needs sign-off.
  { value: "system", short: "Auto", full: "Match my system setting" },
  { value: "light", short: "Light", full: "Always light" },
  { value: "dark", short: "Dark", full: "Always dark" },
];

export type ThemeToggleProps = {
  /**
   * The choice the server read from the cookie.
   *
   * Unlike reading mode, there is no OS preference to settle after mount: the
   * fallback for somebody who has never chosen is handled in CSS by
   * `prefers-color-scheme`, so the server writing no attribute is already the
   * right answer and the first paint is never wrong. This prop only decides
   * which of the three positions is drawn as selected.
   */
  initial?: Theme;
};

export function ThemeToggle({ initial }: ThemeToggleProps) {
  const [choice, setChoice] = useState<Theme>(initial ?? "system");

  /*
   * Reconcile once, for the case the server could not see.
   *
   * A response served from a cache that dropped the cookie, or a browser that
   * clears cookies on close and kept only the localStorage mirror, renders
   * with `initial` undefined and so draws "Auto". If a real choice is on the
   * machine, adopt it and paint it — otherwise the control would claim to be
   * following the system while the reader had asked for something else.
   */
  useEffect(() => {
    const stored = storedTheme();
    if (stored && stored !== choice) {
      setChoice(stored);
      applyTheme(stored);
    }
    // Once, on mount. `choice` is read here but deliberately left out of the
    // dependency list: this reconciles the server's guess, it does not follow
    // later clicks.
  }, []);

  function choose(next: Theme) {
    setChoice(next);
    rememberTheme(next);
  }

  return (
    <fieldset className={styles.group}>
      {/* COPY: needs sign-off. */}
      <legend className={styles.srOnly}>Theme</legend>
      {/*
        The track is a real element rather than the fieldset itself, because a
        <fieldset> lays its contents out through an anonymous box: `display:
        flex` on the fieldset leaves the labels inline, and the control renders
        as three words strung across a box with the selected one shrink-wrapped
        around its own text. See theme.module.css.
      */}
      <div className={styles.track}>
        {OPTIONS.map((option) => (
          <label key={option.value} className={styles.option}>
            <input
              type="radio"
              name="arctic-theme"
              value={option.value}
              checked={choice === option.value}
              onChange={() => choose(option.value)}
              className={styles.input}
            />
            <span className={styles.face}>
              <span className={styles.srOnly}>{option.full}</span>
              <span aria-hidden="true">{option.short}</span>
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
