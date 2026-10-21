"use client";

import { useEffect } from "react";

/**
 * Ask on a clock, but only while somebody could be looking.
 *
 * A hook and not a pair of intervals written out twice, for the reason the
 * dashboard's own comment gave when both of its feeds needed it: the rule --
 * asleep while the tab is hidden, caught up on the way back -- has to hold
 * everywhere, and the second copy of it is the one that would eventually be
 * edited without the first.
 *
 * Lifted out of the dashboard when the check-in desk grew a live count. That
 * count is the same figure on a second screen, so it had to arrive on the same
 * terms: the same ten seconds from `dashboard/checked-in.ts`, and the same
 * silence while nobody is watching. Two screens disagreeing about how live
 * "live" is would be two answers to a question with one right answer.
 *
 * ## Why the tab's visibility is part of the rule
 *
 * Every poll is a query against a Burstable Postgres, and on the one day these
 * screens matter that database is also serving check-in. A board left open on
 * a television behind a screensaver, or a desk tab behind the browser window,
 * must not keep spending queries all day to tell nobody anything.
 *
 * The cost is paid by the screens rather than hidden from them: a feed that
 * was asleep has figures going quietly stale with nothing wrong to report, so
 * what gets shown is decided by the **age of the last figure** and never by
 * whether a request failed. Both readers of this hook work that way.
 *
 * `enabled` is for a feed that only exists to serve something which may not be
 * on screen. A screen showing a refusal should not be polling to refresh it.
 */
export function usePoll(ask: () => void, every: number, enabled = true) {
  useEffect(() => {
    if (!enabled) return;

    const askIfVisible = () => {
      if (document.visibilityState === "visible") void ask();
    };

    const id = setInterval(askIfVisible, every);
    document.addEventListener("visibilitychange", askIfVisible);

    return () => {
      clearInterval(id);
      document.removeEventListener("visibilitychange", askIfVisible);
    };
  }, [ask, every, enabled]);
}
