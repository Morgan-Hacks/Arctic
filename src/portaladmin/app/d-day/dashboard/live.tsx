"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { loadCheckInCounts } from "../actions";
import { loadApplicantAnalytics } from "../../home-actions";
import {
  dateLabel, numbers, statusLabels,
  type ActivityDay, type AnalyticsResult, type AnalyticsView, type SchoolBucket,
} from "../../home-analytics";
import { CHECK_IN_POLL_MS, checkedInTile, type CheckInTile, type Figure } from "./checked-in";
import styles from "./dashboard.module.css";

/**
 * The numbers, refreshed on their own.
 *
 * Once a minute, not once a second. Every poll is a query against a Burstable
 * Postgres, and on the one day this screen matters that database is also
 * serving check-in -- a dashboard that costs the desk its responsiveness has
 * made things worse. A number on a wall is read by somebody glancing up, not
 * watched, and almost nothing here moves faster than that.
 *
 * The exception is the one the issue left open, and this component now makes
 * the call: **the checked-in count runs on its own, faster clock.** It is the
 * number people at the door read while they are working, and it is the only
 * thing on the screen that changes while you watch it. The rest keeps its
 * minute; checked-in.ts holds the interval, and the action behind it holds the
 * reason the fast read is a different request rather than this one asked more
 * often.
 *
 * Polling stops while the tab is hidden and catches up on the way back, so a
 * tab left open behind other work is not quietly spending queries all day.
 *
 * The rule the rest of this component exists for: **a number on a wall is
 * believed, so it may never be stale and silent.** The last good figures stay
 * on screen when a refresh fails -- blanking them would be worse, somebody
 * glancing up needs something -- but they are never shown without saying when
 * they are from, and the saying gets louder the older they get.
 *
 * That is decided by the **age of the numbers, not by whether a request
 * errored**, and the difference is the whole point. A poll that is skipped
 * rather than failed -- which is what the hidden-tab rule above does every
 * time this screen is occluded, or the display sleeps, or the window goes
 * behind something -- leaves perfectly old figures with nothing wrong to
 * report. Keyed on failure, the board would sit there showing yesterday under
 * a cheerful "Updated 14:32". Keyed on age it cannot.
 */

/** How often to ask, awake. */
const POLL_MS = 60_000;

/**
 * How old the numbers may get before the board has to admit it.
 *
 * One missed beat is ordinary -- a poll in flight, a slow query -- and
 * shouting about it would train everybody to ignore the banner. Two is
 * something a reader deserves to know before trusting a figure.
 */
const FRESH_MS = 2 * POLL_MS;

/** Past this, the banner stops being a note and starts being a warning. */
const STALE_MS = 3 * POLL_MS;

/**
 * Ask on a clock, but only while somebody could be looking.
 *
 * Two clocks here rather than one, which is the whole reason this is a hook
 * and not a pair of intervals written out twice: the rule -- asleep while the
 * tab is hidden, caught up on the way back -- has to hold for both feeds, and
 * the second copy of it is the one that would eventually be edited without
 * the first.
 *
 * Both feeds are server actions, and Next dispatches those **one at a time
 * per client**, so the fast feed can queue behind an in-flight slow poll once
 * a minute. That costs a number on this board at most the length of one
 * analytics read, and the tile's own age is what decides whether it says
 * anything -- so the coupling shows up as a slightly older `As of` line, never
 * as a fresh-looking figure nobody has checked. Worth knowing rather than
 * working around: the fix would be a route handler, and trading this
 * convention for a delay nobody can see is not a trade.
 *
 * `enabled` exists because the fast feed exists for one tile. While there is
 * no analytics payload there is no tile to feed, and a board left open on a
 * refusal should not be spending ten-second queries to say nothing to nobody.
 */
function usePoll(ask: () => void, every: number, enabled = true) {
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

export function LiveBoard({ initial, eventId }: { initial: AnalyticsResult; eventId?: string }) {
  // What is on screen, and when it was true. Held separately from the last
  // attempt, which is the whole point: a failed refresh must not take the
  // numbers away, and must not silently keep them either.
  const [good, setGood] = useState<{ view: AnalyticsView; at: number } | null>(
    initial.data ? { view: initial.data, at: Date.parse(initial.updatedAt) } : null,
  );
  const [problem, setProblem] = useState<string | null>(initial.error ?? null);

  // The fast feed's figure, seeded from the payload the page already carries
  // so the tile does not open by claiming a number from nowhere. From here it
  // is refreshed on its own clock, and its age alone decides whether the tile
  // says anything about it -- see checked-in.ts.
  const [checkIn, setCheckIn] = useState<Figure | null>(
    initial.data?.analytics
      ? {
          count: initial.data.analytics.statuses.checked_in ?? 0,
          at: Date.parse(initial.updatedAt),
        }
      : null,
  );

  // Re-rendered on a timer as well as on a poll, so "4 minutes ago" is not
  // itself a stale number while the tab sits there failing to refresh.
  const [now, setNow] = useState(() => Date.now());

  const inFlight = useRef(false);
  const checkInInFlight = useRef(false);

  const refresh = useCallback(async () => {
    if (inFlight.current) return;
    inFlight.current = true;
    try {
      const next = await loadApplicantAnalytics(eventId);
      if (next.data) {
        setGood({ view: next.data, at: Date.parse(next.updatedAt) });
        setProblem(null);
      } else {
        setProblem(next.error);
      }
    } catch {
      // COPY: needs sign-off.
      setProblem("The last refresh did not come back.");
    } finally {
      inFlight.current = false;
      setNow(Date.now());
    }
  }, [eventId]);

  /**
   * The fast feed.
   *
   * A failure sets no state of its own, deliberately. What the tile shows is
   * the age of the last figure it was given, and a failed attempt that leaves
   * that figure a second old is not worth a word -- which is the same rule the
   * banner above works to, learned here the same way: keyed on the attempt
   * rather than on the age, a feed that goes quiet has nothing to report.
   */
  const refreshCheckIn = useCallback(async () => {
    if (checkInInFlight.current) return;
    checkInInFlight.current = true;
    try {
      const next = await loadCheckInCounts(eventId);
      if (next.counts) {
        setCheckIn({ count: next.counts.checked_in ?? 0, at: Date.parse(next.updatedAt) });
      }
    } catch {
      // See above: the age of what is on screen is what says so.
    } finally {
      checkInInFlight.current = false;
      setNow(Date.now());
    }
  }, [eventId]);

  /** Both feeds, because the banner's offer is about the board, not one of them. */
  const retry = useCallback(() => {
    void refresh();
    void refreshCheckIn();
  }, [refresh, refreshCheckIn]);

  // The clock the two freshness readings are read against.
  useEffect(() => {
    const tick = setInterval(() => setNow(Date.now()), 15_000);
    return () => clearInterval(tick);
  }, []);

  usePoll(refresh, POLL_MS);
  usePoll(refreshCheckIn, CHECK_IN_POLL_MS, Boolean(good?.view.analytics));

  // Nothing ever loaded. There is no number to protect, so the refusal from
  // the loader is the whole screen.
  if (!good) {
    return (
      <>
        <h1>Dashboard</h1>
        <div className="empty">{problem ?? "Nothing to show yet."}</div>
      </>
    );
  }

  const { analytics, chosen } = good.view;
  const age = now - good.at;

  // The checked-in figure, whichever of the two feeds is holding the newer
  // one, and what the tile has to say about it. The board's own read is the
  // fallback rather than a blank: when the fast feed stops, the number drops
  // back to the pace the rest of the board runs at, which is slower but is
  // never the silence this screen exists to avoid.
  const boardFigure: Figure | null = analytics
    ? { count: analytics.statuses.checked_in ?? 0, at: good.at }
    : null;
  const checkedIn = checkedInTile(checkIn, boardFigure, now);

  return (
    <>
      <div className={styles.head}>
        <div>
          <h1>Dashboard</h1>
          {chosen ? <p className={styles.event}>{chosen.name}</p> : null}
        </div>
        <Freshness at={good.at} age={age} problem={problem} onRetry={retry} />
      </div>

      {analytics ? (
        <>
          <Tiles analytics={analytics} checkedIn={checkedIn} />
          <div className={styles.panels}>
            <Arrivals activity={analytics.activity} />
            <Schools schools={analytics.schools} />
          </div>
        </>
      ) : (
        <div className="empty">
          {/* COPY: needs sign-off. */}
          No applications for this event yet. The numbers appear as they arrive.
        </div>
      )}
    </>
  );
}

/**
 * When these numbers were last true.
 *
 * Always present, never only on failure. "Updated 14:32" next to a healthy
 * board is what makes the warning legible when it replaces it -- somebody who
 * has only ever seen the warning has no idea what normal looks like.
 */
function Freshness({
  at, age, problem, onRetry,
}: { at: number; age: number; problem: string | null; onRetry: () => void }) {
  const clock = new Intl.DateTimeFormat("en-US", { hour: "numeric", minute: "2-digit" }).format(at);

  if (!problem && age < FRESH_MS) {
    return (
      <p className={styles.fresh} aria-live="off">
        Updated {clock}
      </p>
    );
  }

  // The same rounded figure the sentence shows decides how loud it is. Read
  // off `age` directly these disagree: the label rounds up at two and a half
  // minutes, so a board can read "3 minutes old" while still looking calm.
  const old = Math.max(1, Math.round(age / 60_000));
  const bad = old * 60_000 >= STALE_MS;

  return (
    <div className={styles.stale} data-bad={bad || undefined} role="status">
      {/* COPY: needs sign-off. The time is the load-bearing part: somebody
          deciding whether to trust a count needs to know how old it is, not
          that something unspecified went wrong. */}
      <strong>
        {problem
          ? (bad ? "Not refreshing" : "Could not refresh")
          /* Nothing failed -- the polling is asleep, which a hidden tab does
             on purpose. Still old, so it still has to be said. */
          : "Out of date"}
      </strong>
      <span>Showing {clock}, {minutes(old)} old.</span>
      <button type="button" className={styles.retry} onClick={onRetry}>
        Try now
      </button>
    </div>
  );
}

/**
 * The four numbers worth reading from across a room.
 *
 * `submittedApplications` is every application that came in. The three
 * beside it come from `statuses`, which is a breakdown of where applications
 * are *now* and not a funnel -- an accepted applicant who confirms stops
 * counting as accepted. They are labelled as states for that reason, and
 * nothing here adds them up.
 *
 * Checked in is deliberately not one of `statuses` for long. It takes the
 * newer of the fast feed and the payload, so on a good day it is up to fifty
 * seconds ahead of the three beside it -- the trade the issue named, made
 * because it is the number the door reads while working. The line underneath
 * it is what keeps that honest at a glance: the other three are covered by
 * the banner, and this one says for itself when its feed has gone quiet.
 */
function Tiles({
  analytics, checkedIn,
}: {
  analytics: NonNullable<AnalyticsView["analytics"]>;
  checkedIn: CheckInTile;
}) {
  const { statuses } = analytics;

  return (
    <div className={styles.tiles}>
      <Tile label="Submitted" value={analytics.submittedApplications} lead />
      <Tile label={statusLabels.accepted} value={statuses.accepted ?? 0} />
      <Tile label={statusLabels.confirmed} value={statuses.confirmed ?? 0} />
      <Tile
        label={statusLabels.checked_in}
        value={checkedIn.count ?? statuses.checked_in ?? 0}
        note={checkedIn.note}
      />
    </div>
  );
}

function Tile({
  label, value, lead, note,
}: {
  label: string;
  value: number;
  lead?: boolean;
  note?: { text: string; live: boolean } | null;
}) {
  return (
    <div className={styles.tile} data-lead={lead || undefined}>
      <span className={styles.tileValue}>{numbers.format(value)}</span>
      <span className={styles.tileLabel}>{label}</span>
      {/* COPY: needs sign-off. The time is the whole point of the line --
          a reader deciding whether a number has moved needs to know when it
          was read, not that something unspecified is different.

          No aria-live: this text changes every ten seconds while healthy,
          and a screen reader that announces each of those is one somebody
          turns off, taking the one announcement that mattered with it. */}
      {note ? (
        <span className={styles.tileNote} data-not-live={note.live ? undefined : ""}>
          {note.text}
        </span>
      ) : null}
    </div>
  );
}


/**
 * Applications per day, as plain bars.
 *
 * No chart library. Fourteen numbers and a maximum is a height calculation,
 * and pulling a library in for it would cost more to load on a screen that
 * reloads itself all day than the thing it draws.
 *
 * Submitted rather than started, because a started application is somebody
 * who opened the form and a submitted one is somebody who finished -- only
 * the second is a number worth watching a trend in.
 */
function Arrivals({ activity }: { activity: ActivityDay[] }) {
  const days = activity.slice(-14);
  const most = Math.max(1, ...days.map((day) => day.submitted));

  if (days.length === 0) {
    return (
      <section className={styles.panel}>
        <h2 className={styles.panelTitle}>Applications a day</h2>
        {/* COPY: needs sign-off. */}
        <p className={styles.panelEmpty}>Nothing submitted yet.</p>
      </section>
    );
  }

  return (
    <section className={styles.panel}>
      <h2 className={styles.panelTitle}>Applications a day</h2>
      <ol className={styles.bars}>
        {days.map((day) => (
          <li key={day.date} className={styles.bar}>
            {/* The number is on the bar's label rather than drawn, so a
                reader using a screen reader gets the figure and not a shape. */}
            <span
              className={styles.barFill}
              style={{ height: `${Math.round((day.submitted / most) * 100)}%` }}
              aria-hidden="true"
            />
            <span className={styles.barLabel}>
              <span className={styles.srOnly}>{dateLabel(day.date)}: </span>
              {day.submitted}
            </span>
          </li>
        ))}
      </ol>
      <p className={styles.panelFoot}>
        {dateLabel(days[0].date)} – {dateLabel(days[days.length - 1].date)}
      </p>
    </section>
  );
}

/**
 * Where applicants are coming from.
 *
 * Counts only. Which schools are over- or under-represented is a judgement
 * against a baseline nobody here has written down, and a board that implies
 * one it was never given is worse than a board that lists what arrived.
 */
function Schools({ schools }: { schools: SchoolBucket[] }) {
  const top = schools.slice(0, 6);
  const most = Math.max(1, ...top.map((school) => school.count));

  return (
    <section className={styles.panel}>
      <h2 className={styles.panelTitle}>Schools</h2>
      {top.length === 0 ? (
        /* COPY: needs sign-off. */
        <p className={styles.panelEmpty}>No schools answered yet.</p>
      ) : (
        <ol className={styles.schools}>
          {top.map((school) => (
            <li key={school.label} className={styles.school}>
              <span className={styles.schoolName}>{school.label}</span>
              <span className={styles.schoolTrack} aria-hidden="true">
                <span
                  className={styles.schoolFill}
                  style={{ width: `${Math.round((school.count / most) * 100)}%` }}
                />
              </span>
              <span className={styles.schoolCount}>{numbers.format(school.count)}</span>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

/** Whole minutes, because a wall clock does not need seconds. */
function minutes(total: number): string {
  return total === 1 ? "1 minute" : `${total} minutes`;
}
