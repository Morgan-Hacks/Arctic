/**
 * When the day's screens open.
 *
 * D-Day collects what only matters once doors open. Before then the board
 * reads zero for everything it measures -- nobody has checked in, because
 * check-in has not started -- and a wall of zeroes is not an empty screen, it
 * is a screen that looks broken. So it stays shut until it has something to
 * say, and says when it will open.
 *
 * ## This is a door, not a lock
 *
 * Nothing here is a permission. The board is built out of
 * `GET /admin/analytics/applicants`, which is behind `applications.view` and
 * stays behind it; somebody who types the URL during setup week is not
 * reading anything they could not read on the home screen. What this prevents
 * is the console offering a screen full of zeroes as though it were news.
 */

/** The day before, which is what "the day before" means. */
const DAYS_BEFORE = 1;

export type DDayLock = {
  locked: boolean;

  /** Midnight UTC at the start of the day before. Null when no date is set. */
  opensAt: Date | null;

  startsAt: Date | null;

  /** Whole days from now until it opens. Zero or less once it has. */
  daysAway: number | null;
};

/**
 * Whether the day's screens are still shut, and when they are not.
 *
 * The whole calendar day before rather than twenty-four hours before doors.
 * An event starting at five on Friday would otherwise unlock at five on
 * Thursday, and the people setting the room up on Thursday morning are
 * exactly who needs it. Erring early is the safe direction here: the cost of
 * opening too soon is a quiet screen, and the cost of opening too late is
 * somebody at a desk who cannot see the queue.
 *
 * Measured in UTC, which for Maryland means it opens on the evening of two
 * days before rather than at local midnight. That is the same error in the
 * same safe direction, and it is the honest thing to do while an event has a
 * start instant and no timezone of its own.
 *
 * An event with no start date never locks. A date nobody has agreed yet is
 * not a reason to hide a screen, and there would be nothing to tell somebody
 * about when it would open.
 */
export function dDayLock(
  startsAt: string | Date | null | undefined,
  now: Date = new Date(),
): DDayLock {
  const starts = startsAt ? new Date(startsAt) : null;

  if (!starts || Number.isNaN(starts.getTime())) {
    return { locked: false, opensAt: null, startsAt: null, daysAway: null };
  }

  const opensAt = new Date(
    Date.UTC(
      starts.getUTCFullYear(),
      starts.getUTCMonth(),
      starts.getUTCDate() - DAYS_BEFORE,
    ),
  );

  const daysAway = Math.ceil(
    (opensAt.getTime() - now.getTime()) / (24 * 60 * 60 * 1000),
  );

  return { locked: now < opensAt, opensAt, startsAt: starts, daysAway };
}

/** "tomorrow", "in 3 days" — for a sentence rather than a timestamp. */
export function when(lock: DDayLock): string {
  if (!lock.opensAt) return "";
  if (lock.daysAway !== null && lock.daysAway <= 1) return "tomorrow";
  if (lock.daysAway !== null && lock.daysAway <= 14) return `in ${lock.daysAway} days`;

  return `on ${lock.opensAt.toLocaleDateString(undefined, {
    month: "long",
    day: "numeric",
    timeZone: "UTC",
  })}`;
}
