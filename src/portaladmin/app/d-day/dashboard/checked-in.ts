/**
 * The checked-in figure, and the one number on this board allowed to move
 * faster than the rest of it.
 *
 * The issue behind the board asked what counts as live and answered most of
 * it: once a minute is almost certainly enough for anything except the
 * check-in count. This is that exception, kept in its own file because the
 * rule that decides what the tile says is the whole point and it deserves a
 * test that does not need a browser.
 */

/**
 * How often the check-in figure is asked for.
 *
 * Ten seconds: close enough to "somebody scans, the number moves" that a
 * person watching it after each scan is not left wondering, and far enough
 * from once a second that a tab left open all day costs hundreds of queries
 * rather than tens of thousands. The issue named the cost as the reason to
 * think about this at all and named the check-in count as the one exception;
 * this is that decision, made once and in one place.
 */
export const CHECK_IN_POLL_MS = 10_000;

/**
 * How long the fast feed may go quiet before the tile has to say so.
 *
 * Four missed beats. One is a slow query and two is a poll in flight — the
 * board's own banner gives a missed beat the same latitude, and a tile that
 * shouts on ordinary is a tile nobody reads. Four means the feed has stopped
 * and the number on screen is no longer coming in live, which is the one
 * thing a reader at the desk cannot work out for themselves.
 */
export const CHECK_IN_QUIET_MS = 4 * CHECK_IN_POLL_MS;

/** A number and the moment it was true. */
export type Figure = { count: number; at: number };

export type CheckInTile = {
  /** The figure to show, or null when neither feed has ever answered. */
  count: number | null;
  /**
   * What goes under the figure. Always present while there is a figure,
   * for the same reason the banner's `Updated 14:32` is: somebody who has
   * only ever seen the warning has no idea what normal looks like.
   */
  note: { text: string; live: boolean } | null;
};

const clock = new Intl.DateTimeFormat("en-US", {
  hour: "numeric",
  minute: "2-digit",
  second: "2-digit",
});

/**
 * What the checked-in tile shows.
 *
 * `feed` is the fast read; `board` is the figure the board's own minute-old
 * analytics payload carries. The newest of the two wins, and the difference
 * matters: when the fast feed stops, the tile falls back to the board's read
 * rather than sitting on its last good number, so the worst case is a count
 * a minute old — the pace the rest of the board runs at, and never the
 * silence this screen exists to avoid.
 *
 * The note is decided by the **age of the fast feed**, not by whether its
 * last attempt errored, which is the rule the board's banner learned the hard
 * way: a poll that was skipped rather than failed — a hidden tab, a sleeping
 * display, a window behind another — leaves a number going quietly stale with
 * nothing wrong to report.
 */
export function checkedInTile(
  feed: Figure | null,
  board: Figure | null,
  now: number,
): CheckInTile {
  const shown = feed && (!board || feed.at >= board.at) ? feed : board;

  if (!shown) {
    return { count: null, note: null };
  }

  const live = feed !== null && now - feed.at < CHECK_IN_QUIET_MS;
  const time = clock.format(shown.at);

  return {
    count: shown.count,
    // "Not live" rather than "out of date": the figure beside it may be
    // seconds old, taken off the board's own read. What has stopped is the
    // feed, and the sentence should say that and not imply the number is old
    // when it is not.
    note: { text: live ? `As of ${time}` : `As of ${time} · not live`, live },
  };
}
