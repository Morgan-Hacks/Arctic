"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { loadCheckInCounts, type CheckInCountsResult } from "../d-day/actions";
import {
  CHECK_IN_POLL_MS,
  checkedInTile,
  type Figure,
} from "../d-day/dashboard/checked-in";
import { usePoll } from "../d-day/poll";
import { numbers } from "../home-analytics";
import styles from "./desk.module.css";

/**
 * How many people are in, on the screen of the person letting them in.
 *
 * The same number as the dashboard's tile, read the same way, on the same
 * clock: `loadCheckInCounts` for the figure, `checked-in.ts` for how often to
 * ask and for what the line underneath is allowed to say. Nothing about "how
 * live is live" is decided in this file, and that is the point -- the desk and
 * the board are two screens showing one number, and two answers to that
 * question is the failure worth avoiding. If ten seconds turns out to be
 * wrong, it is wrong in one place.
 *
 * ## Why a scan does not nudge it
 *
 * It would be easy to refresh this the moment the scanner gets a `checkedIn`
 * back, and tempting, because the number bumping is satisfying. It is also a
 * second clock wearing a disguise: the count would then move for the scans
 * this browser made and lag for the four other volunteers' doors, which reads
 * as "the number is wrong" to anybody comparing two desks. The answer to a
 * scan is the scan's own sentence, which is immediate and is about one person.
 * This is the room's figure and it arrives on the room's clock.
 */
export function CheckedInCount({
  initial,
  eventId,
}: {
  initial: CheckInCountsResult;
  eventId?: string;
}) {
  // The figure and when it was true, held apart from the last attempt. A
  // failed poll must not take the number away and must not quietly keep it
  // either -- the tile's own rule, in checked-in.ts, decides which.
  const [feed, setFeed] = useState<Figure | null>(
    initial.counts
      ? {
          count: initial.counts.checked_in ?? 0,
          at: Date.parse(initial.updatedAt),
        }
      : null,
  );

  // Only ever the first read's refusal, and never replaced by a later one:
  // once there is a figure the tile speaks for itself. A 403 here also means
  // the roster beside it is already saying the same thing in more words.
  const refused = initial.error ?? null;

  const [now, setNow] = useState(() => Date.now());
  const inFlight = useRef(false);

  const refresh = useCallback(async () => {
    if (inFlight.current) return;
    inFlight.current = true;
    try {
      const next = await loadCheckInCounts(eventId);
      if (next.counts) {
        setFeed({
          count: next.counts.checked_in ?? 0,
          at: Date.parse(next.updatedAt),
        });
      }
    } catch {
      // Deliberately nothing. What this tile shows is the age of the last
      // figure it was given; an attempt that failed and left that figure a
      // second old is not worth a word, and an attempt that was never made --
      // a hidden tab -- would have had nothing to report at all.
    } finally {
      inFlight.current = false;
      setNow(Date.now());
    }
  }, [eventId]);

  // The clock the freshness reading is read against, so "not live" appears on
  // a tab nobody has touched rather than waiting for the next answer.
  useEffect(() => {
    const tick = setInterval(() => setNow(Date.now()), 5_000);
    return () => clearInterval(tick);
  }, []);

  // Not while the first read was refused. A 403 does not become a 200 by
  // being asked again every ten seconds for the length of an event.
  usePoll(refresh, CHECK_IN_POLL_MS, refused === null);

  // No board read on this screen, so the fast feed is the only figure there
  // is. The dashboard passes its minute-old payload as the fallback; here the
  // honest answer to a dead feed is the last number and a line saying so.
  const tile = checkedInTile(feed, null, now);

  if (tile.count === null) {
    return (
      <section className={styles.count} aria-labelledby="checked-in-count">
        <h2 className={styles.countLabel} id="checked-in-count">
          Checked in
        </h2>
        <p className={styles.countEmpty}>
          {refused ?? "The count could not be loaded."}
        </p>
      </section>
    );
  }

  return (
    <section className={styles.count} aria-labelledby="checked-in-count">
      <h2 className={styles.countLabel} id="checked-in-count">
        Checked in
      </h2>
      {/* No aria-live. This figure changes every ten seconds on a busy
          morning, and a screen reader announcing each of those is one
          somebody turns off -- taking the announcements that mattered,
          the scan answers, with it. */}
      <span className={styles.countValue}>{numbers.format(tile.count)}</span>
      {tile.note ? (
        <span
          className={styles.countNote}
          data-not-live={tile.note.live ? undefined : ""}
        >
          {tile.note.text}
        </span>
      ) : null}
    </section>
  );
}
