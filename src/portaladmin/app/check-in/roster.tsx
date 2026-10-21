"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { StatusPill } from "@/components/applicants/status";
import type { ApplicantRow, ApplicantsView } from "@/components/applicants/types";
import type { ViewRead } from "../applicants/api";
import { rosterNote } from "./roster-note";
import styles from "./desk.module.css";

/**
 * Who is on the list, for the times the scanner is no help.
 *
 * The issue's reason for this panel is the one worth keeping in mind while
 * reading it: a scanner fails and somebody still has to be let in. A dead
 * phone, a cracked screen, a hacker who never opened the portal -- the desk
 * cannot answer any of those with a code, so it answers with a name.
 *
 * What it does **not** do is check anybody in. There is no API path for
 * admitting somebody without their code, and the console does not invent one:
 * `POST /admin/check-in/scan` takes a code and the lifecycle only lets a
 * confirmed application become checked in. So this panel tells a volunteer who
 * this person is and what state they are in, and the sentence for each state
 * comes from `roster-note.ts`, which keeps its wording in step with the
 * refusals atlas sends the scanner.
 *
 * ## The three statuses, and the ones left out
 *
 * `DOOR_STATUSES` -- confirmed, checked in, accepted -- and the omissions are
 * deliberate, for a reason written out in that file: this screen is held up at
 * chest height in front of the person it is about, and which flavour of "no"
 * somebody is in is not for it. A name that is not in these results is a name
 * an organizer deals with away from the queue.
 *
 * ## Searching
 *
 * The API's search, not a filter over a page of rows. A desk types three
 * letters of a surname and needs the match from the whole event, not from the
 * fifty rows that happened to arrive first -- and `GET /admin/applicants`
 * already takes a term. Typing waits a beat before asking, because a search
 * per keystroke is five queries to answer one question on the morning that
 * database is also serving the door.
 */

/**
 * How long a volunteer stops typing before the question is asked.
 *
 * A third of a second: longer than the gap between two letters typed quickly,
 * shorter than the pause before somebody looks up at the screen expecting an
 * answer.
 */
const SETTLE_MS = 350;

export function Roster({
  initial,
  search,
}: {
  initial: ApplicantsView;

  /** Bound to this event on the server, so a search cannot answer about another. */
  search: (q: string) => Promise<ViewRead>;
}) {
  const [typed, setTyped] = useState("");
  const [view, setView] = useState(initial);
  const [problem, setProblem] = useState<string | null>(null);
  const [asking, setAsking] = useState(false);

  // Which question the answer on screen belongs to. Two searches in flight
  // come back in whatever order the network decides, and without this the
  // slower one wins -- a desk reading rows for "ol" after typing "olaj".
  const latest = useRef(0);

  // Whether anybody has typed yet. The first page is rendered on the server,
  // so an effect that ran on mount would ask the same question again before
  // the volunteer has done anything -- two queries to draw one list, on the
  // one morning that query is expensive.
  const touched = useRef(false);

  useEffect(() => {
    if (!touched.current) return;

    const term = typed.trim();
    const mine = ++latest.current;

    const ask = setTimeout(async () => {
      setAsking(true);

      let read: ViewRead;
      try {
        read = await search(term);
      } catch {
        if (mine === latest.current) {
          setProblem("The roster could not be loaded. Try again.");
          setAsking(false);
        }
        return;
      }

      if (mine !== latest.current) return;

      if (read.ok) {
        setView(read.view);
        setProblem(null);
      } else {
        // The API's own sentence, which for a 403 names the permission -- the
        // one refusal a volunteer can get something done about.
        setProblem(read.error);
      }

      setAsking(false);
    }, SETTLE_MS);

    return () => clearTimeout(ask);
  }, [typed, search]);

  const counts = view.counts;

  return (
    <section className={styles.roster} aria-labelledby="roster-heading">
      <div className={styles.rosterHead}>
        <h2 className={styles.panelTitle} id="roster-heading">
          Door list
        </h2>
        {/* The three figures this read already carries, for the event rather
            than for the search. Counts that moved with the search would only
            ever confirm what the search already said. */}
        <p className={styles.rosterCounts}>
          {counts.confirmed ?? 0} confirmed · {counts.checked_in ?? 0} checked
          in · {counts.accepted ?? 0} accepted, not confirmed
        </p>
      </div>

      <div className={styles.rosterSearch}>
        <label htmlFor="roster-q">Search the list</label>
        {/* No autoFocus, on purpose. The code box owns the keyboard: a desk
            working through a queue types codes, and a search field that
            grabbed focus on load would send the first scan into this box. */}
        <input
          id="roster-q"
          type="search"
          value={typed}
          autoComplete="off"
          placeholder="Name, email or school"
          onChange={(event) => {
            touched.current = true;
            setTyped(event.target.value);
          }}
        />
        <p className={styles.rosterHint} aria-live="polite">
          {asking ? "Searching…" : null}
        </p>
      </div>

      {problem ? (
        <p className={styles.rosterProblem} role="alert">
          {problem}
        </p>
      ) : view.items.length === 0 ? (
        <p className={styles.rosterEmpty}>
          {typed.trim() === ""
            ? "Nobody is on the door list for this event yet."
            : "Nobody on the door list matches that. This list holds everybody who is confirmed, already in, or accepted and not confirmed — anybody else needs an organizer rather than the desk."}
        </p>
      ) : (
        <ul className={styles.rows}>
          {view.items.map((item) => (
            <Row key={item.id} item={item} />
          ))}
        </ul>
      )}

      {view.nextCursor !== null && view.items.length > 0 ? (
        <p className={styles.rosterHint}>
          {/* No "load more". Turning pages at a door is not how anybody finds
              one person, and the search reads the whole event -- so the
              honest thing to say is that the rest is a keystroke away rather
              than a click. */}
          More than this on the list. Type a name to find somebody in
          particular.
        </p>
      ) : null}
    </section>
  );
}

/**
 * One person, and what the desk does about them.
 *
 * The name is the largest thing on the row for the same reason it is the
 * largest thing in a scan answer: it is what gets checked against the person
 * standing there. The note underneath is the state, in the API's words where
 * the API has any.
 */
function Row({ item }: { item: ApplicantRow }) {
  const note = rosterNote(item.status);

  return (
    <li className={styles.row} data-code={note.code}>
      <div className={styles.rowWho}>
        {/* A link to the record, because the next thing somebody needs about
            a person the desk cannot admit is their history, and it is one
            click away everywhere else in this console. */}
        <Link href={`/applicants/${item.id}`} className={styles.rowName}>
          {name(item)}
        </Link>
        <span className={styles.rowMeta}>
          {item.email}
          {item.school ? ` · ${item.school}` : ""}
        </span>
      </div>

      <div className={styles.rowState}>
        <StatusPill status={item.status} />
        <span className={styles.rowNote}>{note.text}</span>
      </div>
    </li>
  );
}

/** A name to read out, or the address when the record has none. */
function name(item: ApplicantRow): string {
  const full = [item.firstName, item.lastName].filter(Boolean).join(" ").trim();
  return full === "" ? item.email : full;
}
