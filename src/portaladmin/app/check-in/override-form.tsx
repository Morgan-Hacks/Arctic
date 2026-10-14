"use client";

import { useActionState, useState } from "react";
import {
  admitWithoutCode,
  correctName,
  findOverride,
} from "./actions";
import type { AdmitState, CorrectState, FindState, OverrideMatch } from "./actions";

/**
 * The override holder's view: find them, let them in, fix the name.
 *
 * A second flow beside the code box, and deliberately not a part of
 * it — the two are gated on different permissions and the person
 * holding this one is not necessarily the one scanning. Nothing here
 * can move an application between statuses: there is no such field in
 * any request, so "mark them accepted" is not a button that was left
 * out, it is a sentence the screen cannot say.
 *
 * A reason is required on both writes, in the field and again at the
 * API. An override with no reason attached is indistinguishable from
 * a mistake when somebody reads it back in March.
 */
export function OverridePanel() {
  const [found, findAction, findPending] = useActionState(findOverride, {});
  const [admitted, admitAction, admitPending] = useActionState(admitWithoutCode, {});
  const [corrected, correctAction, correctPending] = useActionState(correctName, {});

  const [selected, setSelected] = useState<OverrideMatch | null>(null);

  const statusLabel = (match: OverrideMatch) =>
    match.status === "checked_in"
      ? "Checked in"
      : match.status.replaceAll("_", " ");

  return (
    <div>
      {/* Find. Their own answer stays on screen until the next search. */}
      <form action={findAction}>
        <div>
          <label htmlFor="override-q">Name or email</label>
          <input
            id="override-q"
            name="q"
            required
            autoComplete="off"
            spellCheck={false}
            placeholder="e.g. Lovelace"
            disabled={findPending}
          />
        </div>
        <div style={{ marginTop: 12 }}>
          <button type="submit" className="button primary" disabled={findPending}>
            {findPending ? "Finding…" : "Find"}
          </button>
        </div>
      </form>

      {found.error ? (
        <p role="alert" style={{ marginTop: 12 }}>
          {found.error}
        </p>
      ) : null}

      {found.matches && found.matches.length === 0 ? (
        <p style={{ marginTop: 12 }}>
          Nobody by that name. Check the spelling, or look in Applicants.
        </p>
      ) : null}

      {found.matches && found.matches.length > 0 ? (
        <ul style={{ listStyle: "none", padding: 0, marginTop: 16 }}>
          {found.matches.map((match) => (
            <li
              key={match.applicationId}
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                gap: 12,
                padding: "10px 0",
                borderTop: "1px solid",
              }}
            >
              <span>
                <strong style={{ fontSize: 18 }}>{match.name ?? "No name on file"}</strong>
                <span style={{ marginLeft: 8 }}>{statusLabel(match)}</span>
                {match.eventName || match.event ? (
                  <span style={{ marginLeft: 8, opacity: 0.7 }}>
                    {match.eventName ?? match.event}
                  </span>
                ) : null}
                {match.checkedInAt ? (
                  <span style={{ marginLeft: 8, opacity: 0.7 }}>
                    in since {new Date(match.checkedInAt).toLocaleTimeString()}
                  </span>
                ) : null}
              </span>
              <button
                type="button"
                className="button"
                onClick={() => setSelected(match)}
                disabled={selected?.applicationId === match.applicationId}
              >
                {selected?.applicationId === match.applicationId ? "Chosen" : "Choose"}
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {/* The two writes. Both name their reason out loud, because both
          end up in a trail where the reason is the whole point. */}
      {selected ? (
        <div style={{ marginTop: 24 }}>
          <h2 style={{ marginBottom: 8 }}>{selected.name ?? "No name on file"}</h2>

          <form action={admitAction}>
            <input type="hidden" name="applicationId" value={selected.applicationId} />
            <div>
              <label htmlFor="admit-reason">Why are you overriding?</label>
              <input
                id="admit-reason"
                name="reason"
                required
                autoComplete="off"
                placeholder="e.g. Screen cracked, checked against the roster"
                disabled={admitPending}
              />
            </div>
            <div style={{ marginTop: 12 }}>
              <button type="submit" className="button primary" disabled={admitPending}>
                {admitPending ? "Letting in…" : "Let them in without a code"}
              </button>
            </div>
          </form>

          <form action={correctAction} style={{ marginTop: 24 }}>
            <input type="hidden" name="applicationId" value={selected.applicationId} />
            <div style={{ display: "flex", gap: 12, flexWrap: "wrap" }}>
              <span>
                <label htmlFor="correct-first">First name</label>
                {/* Keyed on the application so the fields refill from
                    the newly chosen person rather than holding the
                    last one's answer. */}
                <input
                  key={`first-${selected.applicationId}`}
                  id="correct-first"
                  name="firstName"
                  required
                  defaultValue={(selected.name ?? "").split(" ")[0] ?? ""}
                  disabled={correctPending}
                />
              </span>
              <span>
                <label htmlFor="correct-last">Last name</label>
                <input
                  key={`last-${selected.applicationId}`}
                  id="correct-last"
                  name="lastName"
                  required
                  defaultValue={(selected.name ?? "").split(" ").slice(1).join(" ")}
                  disabled={correctPending}
                />
              </span>
            </div>
            <div style={{ marginTop: 12 }}>
              <label htmlFor="correct-reason">Why are you correcting it?</label>
              <input
                id="correct-reason"
                name="reason"
                required
                autoComplete="off"
                placeholder="e.g. Badge printed from the wrong spelling, ID checked"
                disabled={correctPending}
              />
            </div>
            <div style={{ marginTop: 12 }}>
              <button type="submit" className="button" disabled={correctPending}>
                {correctPending ? "Correcting…" : "Correct the name"}
              </button>
            </div>
          </form>
        </div>
      ) : null}

      {/* The answers, in the API's own words. */}
      {admitted.message ? (
        <div
          role="status"
          aria-live="polite"
          style={{ marginTop: 16, padding: 12, border: "1px solid", borderRadius: 8 }}
        >
          <p style={{ margin: 0, fontWeight: 600 }}>{admitted.message}</p>
          {admitted.name ? (
            <p style={{ margin: "8px 0 0", fontSize: 20 }}>{admitted.name}</p>
          ) : null}
          {admitted.alreadyCheckedIn && admitted.checkedInAt ? (
            <p style={{ margin: "4px 0 0" }}>
              Already in since {new Date(admitted.checkedInAt).toLocaleString()}. Let them
              through.
            </p>
          ) : null}
        </div>
      ) : null}

      {admitted.error ? (
        <div
          role="alert"
          style={{ marginTop: 16, padding: 12, border: "1px solid", borderRadius: 8 }}
        >
          <p style={{ margin: 0, fontWeight: 600 }}>{admitted.error}</p>
          {admitted.name ? (
            <p style={{ margin: "8px 0 0", fontSize: 20 }}>{admitted.name}</p>
          ) : null}
        </div>
      ) : null}

      {corrected.name ? (
        <div
          role="status"
          aria-live="polite"
          style={{ marginTop: 16, padding: 12, border: "1px solid", borderRadius: 8 }}
        >
          <p style={{ margin: 0, fontWeight: 600 }}>Name corrected.</p>
          <p style={{ margin: "8px 0 0", fontSize: 20 }}>{corrected.name}</p>
        </div>
      ) : null}

      {corrected.error ? (
        <p role="alert" style={{ marginTop: 12 }}>
          {corrected.error}
        </p>
      ) : null}
    </div>
  );
}
