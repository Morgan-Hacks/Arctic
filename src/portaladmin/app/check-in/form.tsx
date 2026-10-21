"use client";

import { useActionState, useCallback, useEffect, useRef, useState } from "react";
import { Camera } from "./camera";
import { scanCheckIn } from "./actions";
import styles from "./desk.module.css";

/**
 * The volunteer's view: one input, one answer.
 *
 * Built for a queue — the input keeps focus so the next code can be
 * typed the moment the last answer lands, and the answer stays on
 * screen until the next scan replaces it. The name is the largest
 * thing in the answer: a code that never rotates is useless
 * forwarded only because somebody reads this while looking at
 * whoever handed over the phone.
 *
 * The camera sits above the box and feeds it. A code read off a QR
 * symbol is put in this input and submitted through the same server
 * action a typed one goes through, so there is one path to the API
 * and one set of answers — see `camera.tsx` for why the camera is an
 * offer rather than the way in.
 *
 * ## The network, said out loud
 *
 * Everything else on this screen is a sentence from the API. The
 * exception is the one state the API cannot report, because it never
 * heard the question: the venue's wifi going. `actions.ts` retries a
 * scan a couple of times before admitting it, and what arrives here
 * is `unreachable` — which this component treats unlike every other
 * failure. The typed code stays in the box, nothing is cleared, and
 * the panel is the loudest thing on screen.
 *
 * Loud on purpose, and not held quietly for later. A volunteer who
 * can see that a scan did not land asks the next person to wait; a
 * queue that swallowed the last forty scans and flushes them at
 * lunchtime leaves a count nobody can trust and a morning nobody can
 * reconstruct. The desk is the one place in this console with a human
 * standing at it, which is exactly why the failure belongs in front
 * of them rather than in a buffer.
 */
export function ScanForm({ hasRoster }: { hasRoster: boolean }) {
  const [state, action, pending] = useActionState(scanCheckIn, {});
  const form = useRef<HTMLFormElement>(null);
  const input = useRef<HTMLInputElement>(null);

  // Controlled, which it was not before, for one reason: React clears an
  // uncontrolled field when a form action settles. That is right for every
  // answer the API gives — the next person is next — and wrong for the one
  // it does not, where clearing the box would throw away a code somebody
  // read off a phone that has already gone back in a pocket.
  const [code, setCode] = useState("");

  // What the browser itself knows. The action can only report a round trip
  // that failed; this reports a device with no network at all, before a
  // volunteer has pressed anything.
  const [offline, setOffline] = useState(false);

  // Set when the camera put the code there, so the effect below knows to
  // submit rather than wait for a keystroke it will never get.
  const fromCamera = useRef(false);

  // After every answer: empty the box and put the cursor back in it, because
  // the next person is next and a desk that makes the volunteer click before
  // each of four hundred scans is broken. The exception is the network, where
  // the code is put back instead -- an answer nobody got is not a person
  // dealt with.
  useEffect(() => {
    setCode(state.unreachable ? state.code ?? "" : "");
    input.current?.focus();
    input.current?.select();
  }, [state]);

  useEffect(() => {
    const read = () => setOffline(!navigator.onLine);

    // Read after mount rather than during the render: the server has no
    // navigator, and markup that guesses would be markup the browser
    // disagrees with.
    read();
    window.addEventListener("online", read);
    window.addEventListener("offline", read);

    return () => {
      window.removeEventListener("online", read);
      window.removeEventListener("offline", read);
    };
  }, []);

  // Submitted from an effect and not from the camera's callback, because the
  // input is controlled: at the moment the code is read React has not yet
  // put it in the DOM, and a form submitted then would send the last code
  // rather than this one.
  useEffect(() => {
    if (!fromCamera.current || code === "") return;
    fromCamera.current = false;
    form.current?.requestSubmit();
  }, [code]);

  const onCode = useCallback((read: string) => {
    fromCamera.current = true;
    setCode(read);
  }, []);

  const retry = useCallback(() => form.current?.requestSubmit(), []);

  const ok = state.outcome === "checkedIn" || state.outcome === "alreadyCheckedIn";
  const refused = state.outcome === "notConfirmed" || state.outcome === "unknownCode";

  return (
    <div className={styles.scanner}>
      {/* Above the input and above the answer, because it is about every
          scan rather than about the last one. */}
      {offline || state.unreachable ? (
        <div className={styles.alarm} role="alert">
          {/* COPY: needs sign-off. The second sentence is the load-bearing
              one: a volunteer has to know that nothing is being held for
              them, or they will wave the queue through believing the desk
              will catch up later. */}
          <strong>
            {offline
              ? "This device is offline. Nothing is being checked in."
              : "The desk could not reach the API. That scan did not land."}
          </strong>
          <span>
            Nothing is queued and nothing will be sent later. The code is
            still in the box — press Check in again once the network is back,
            and read the answer before letting anybody through.
          </span>
          {state.unreachable ? (
            <button type="button" className="button" onClick={retry} disabled={pending}>
              {pending ? "Trying…" : "Try again"}
            </button>
          ) : null}
        </div>
      ) : null}

      <Camera onCode={onCode} busy={pending} />

      <form action={action} ref={form}>
        <div>
          <label htmlFor="checkin-code">Check-in code</label>
          <input
            ref={input}
            id="checkin-code"
            name="code"
            required
            autoComplete="off"
            autoFocus
            spellCheck={false}
            autoCapitalize="characters"
            placeholder="e.g. K7QM 4XPT 9BD2"
            disabled={pending}
            className={styles.code}
            value={code}
            onChange={(event) => setCode(event.target.value)}
          />
        </div>

        <div className={styles.submit}>
          <button type="submit" className="button primary" disabled={pending}>
            {pending ? "Checking…" : "Check in"}
          </button>
        </div>
      </form>

      {/* The answer, in the API's own words. */}
      {state.message && ok ? (
        <div className={styles.answer} data-tone="ok" role="status" aria-live="polite">
          <p className={styles.answerSentence}>{state.message}</p>
          {state.name ? <p className={styles.answerName}>{state.name}</p> : null}
          {state.outcome === "alreadyCheckedIn" && state.checkedInAt ? (
            <p className={styles.answerMore}>
              Already in since {new Date(state.checkedInAt).toLocaleString()}. Let them through.
            </p>
          ) : null}
        </div>
      ) : null}

      {state.error && refused ? (
        <div className={styles.answer} data-tone="refused" role="alert">
          <p className={styles.answerSentence}>{state.error}</p>
          {state.name ? <p className={styles.answerName}>{state.name}</p> : null}
          {state.outcome === "notConfirmed" ? (
            <p className={styles.answerMore}>Send them to an organizer.</p>
          ) : null}
        </div>
      ) : null}

      {/* Everything else: a session that has ended, a permission that is
          gone, a body that did not parse. Not the network, which has its own
          panel above and must not read as one more thing that went wrong. */}
      {state.error && !refused && !state.unreachable ? (
        <p className={styles.answerPlain} role="alert">
          {state.error}
        </p>
      ) : null}

      <p className={styles.fallback}>
        {hasRoster
          ? "No code? Find them on the door list on this screen — a dead phone is not a refusal."
          : "No code? Ask an organizer who can read the applicant list to look them up — a dead phone is not a refusal."}
      </p>
    </div>
  );
}
