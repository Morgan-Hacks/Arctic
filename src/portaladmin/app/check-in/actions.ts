"use server";

import { apiFetch } from "@/lib/api";
import { readView, type Filter, type ViewRead } from "../applicants/api";
import { DOOR_STATUSES } from "./roster-note";

/**
 * The door's half of check-in, in the console.
 *
 * One screen hitting the one route atlas already serves —
 * POST /admin/check-in/scan, gated on checkin.scan. The code is
 * typed, pasted, or read off a QR symbol by the camera — and all
 * three arrive here, at this action, because a second way into the
 * API is a second set of answers to keep in step. The
 * /admin/check-in/override routes, for the person whose code will not
 * scan, are further down.
 *
 * The API's sentences are shown verbatim. They were written for
 * somebody standing in a doorway with a queue behind them, so this
 * file does not reword them and does not invent its own.
 *
 * ## What happens when the venue wifi goes
 *
 * The issue called this a real decision rather than a detail, and it
 * is: a hackathon venue's network is the least reliable part of the
 * day and this screen needs a round trip per person.
 *
 * The decision made here is **forgiving retry and a loud failure, and
 * deliberately not an offline queue.** A queue that holds check-ins
 * silently is worse than a visible failure, because the failure has a
 * human standing at it. A volunteer who can see that the scan did not
 * land asks the next person to wait ten seconds, or writes a name on
 * paper; a queue that swallowed forty scans and flushes them an hour
 * later leaves the count wrong all morning, double-counts anybody who
 * was re-scanned in the meantime, and nobody at the door ever knew.
 * The failure mode of the honest version is a slow queue. The failure
 * mode of the queue is a wrong number nobody can audit until the day
 * is over.
 *
 * So: a transport failure is retried a couple of times here, where
 * the retries cost nothing and nobody is watching, and anything that
 * survives that is handed back as `unreachable` for the screen to
 * shout about while keeping the code in the box. If the maintainer
 * wants a queue, it belongs behind a flag with a visible count of
 * what is held and a way to flush it by hand — not in the quiet path
 * this file takes.
 */

/**
 * How many times a scan is sent before the desk is told.
 *
 * Three attempts over about a second. Retried only when the request
 * never got an answer — a refusal, a 404 or a 409 is the API
 * speaking, and sending that again would be asking the same question
 * twice in the hope of a different answer.
 *
 * Retrying a scan is safe in the one way that matters: if the first
 * attempt actually landed and only the answer was lost, the second
 * gets `alreadyCheckedIn` and a 200, because atlas treats a repeat
 * scan as a normal event rather than a fault. The volunteer reads
 * "already checked in" instead of "checked in", which is true, and
 * nothing is written twice.
 */
const ATTEMPTS = 3;

/** The waits between those attempts. One entry shorter than ATTEMPTS. */
const BACKOFF_MS = [200, 600];

export type ScanOutcome =
  | "checkedIn"
  | "alreadyCheckedIn"
  | "notConfirmed"
  | "unknownCode";

export type ScanState = {
  error?: string;
  outcome?: ScanOutcome;
  /** The API's sentence for the 200s. */
  message?: string;
  /** Who the code belongs to — read it while looking at who handed over the phone. */
  name?: string | null;
  checkedInAt?: string | null;
  /** Echoed so the screen can say which code this answer is about. */
  code?: string;

  /**
   * The network, not the API.
   *
   * A separate flag rather than a fifth `ScanOutcome`, because that
   * union is atlas's vocabulary and this is the one answer atlas
   * never gave. The screen treats it differently from every other
   * failure: the code stays in the box, nothing is counted, and the
   * state is loud enough that nobody waves the next person forward
   * believing the last one is in.
   */
  unreachable?: true;
};

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

export async function scanCheckIn(
  _previous: ScanState,
  form: FormData,
): Promise<ScanState> {
  const code = text(form, "code");

  if (code === "") {
    return { error: "Scan or type a code." };
  }

  let response: Response | null = null;

  for (let attempt = 0; attempt < ATTEMPTS && response === null; attempt++) {
    if (attempt > 0) {
      await new Promise((wake) =>
        setTimeout(wake, BACKOFF_MS[attempt - 1] ?? 0),
      );
    }

    try {
      response = await apiFetch("/admin/check-in/scan", {
        method: "POST",
        body: JSON.stringify({ code }),
        headers: { "content-type": "application/json" },
      });
    } catch {
      // Nothing to say yet. Every attempt but the last is a retry, and
      // a volunteer does not need to hear about a round trip that was
      // already made good inside a second.
    }
  }

  if (response === null) {
    // The same sentence as before, because it is still the right one:
    // it names the thing to do. What is new is the flag beside it,
    // which is what lets the screen keep the code and say so loudly
    // rather than treating this like a refusal.
    return { error: "The API could not be reached. Try again.", code, unreachable: true };
  }

  if (response.status === 401) {
    return { error: "Your session has ended. Sign in again.", code };
  }

  if (response.status === 403) {
    return { error: "You do not have checkin.scan. Ask an admin.", code };
  }

  // 200 (checkedIn / alreadyCheckedIn), 409 (notConfirmed),
  // 404 (unknownCode) and 400 all carry their own sentence.
  try {
    const body = (await response.json()) as {
      outcome?: ScanOutcome;
      message?: string;
      error?: string;
      name?: string | null;
      checkedInAt?: string | null;
    };

    if (!response.ok) {
      return {
        outcome: body.outcome,
        error: body.error ?? "That did not work.",
        name: body.name ?? null,
        code,
      };
    }

    return {
      outcome: body.outcome,
      message: body.message,
      name: body.name ?? null,
      checkedInAt: body.checkedInAt ?? null,
      code,
    };
  } catch {
    return { error: "That did not work.", code };
  }
}

/* ------------------------------------------------------------ roster ---
 *
 * The other half of the desk, and a different permission.
 *
 * `GET /admin/applicants` through the console's existing wrapper for
 * it, rather than anything new. That read already takes a search
 * term, already pages, already scopes itself to an event and already
 * knows what to say about a 403 — and the alternative, a
 * check-in-shaped read of the same table, would be a second place for
 * "who is coming" to be answered slightly differently.
 *
 * It is behind `applications.view`, which is **not** the permission
 * this screen's scanner needs. A volunteer holding only `checkin.scan`
 * gets the refusal `why()` writes, and the desk renders without a
 * roster rather than breaking — see page.tsx, where that split is
 * made and explained.
 */

/** What to ask for: this event, this term, and the door's statuses. */
function doorFilter(event: string | undefined, q: string): Filter {
  return {
    event,
    // An empty search is the whole door list, not a search for "".
    q: q.trim() === "" ? undefined : q.trim(),
    status: [...DOOR_STATUSES],
  };
}

/**
 * Who is on the list, for a name typed at the desk.
 *
 * The event is bound on the server by the page, the way the applicants
 * list binds it into "load more": a search sent with whatever event the
 * browser believed it was looking at is a search that can answer about
 * a different event.
 */
export async function searchRoster(
  event: string | undefined,
  q: string,
): Promise<ViewRead> {
  return readView(doorFilter(event, q));
}

// ------------------------------------------------------------- the override ---
/**
 * The override half of the desk: finding somebody whose code will not
 * scan, letting them in anyway, and correcting a name the portal has
 * locked. All three behind checkin.override, which is deliberately not
 * checkin.scan — the volunteer scanning badges and the person authorised
 * to override are not always the same person.
 *
 * A reason is on both writes and the API refuses without one. An
 * override with no reason attached is indistinguishable from a mistake
 * when somebody reads it back in March, so the field is required here
 * as well as there rather than only where the trail happens to notice.
 */

export type OverrideMatch = {
  applicationId: string;
  status: string;
  name: string | null;
  checkedInAt: string | null;
  eventId?: string | null;
  eventName?: string | null;
};

export type FindState = {
  error?: string;
  matches?: OverrideMatch[];
};

export type AdmitState = {
  error?: string;
  /** The API's sentence for the 200s. */
  message?: string;
  name?: string | null;
  alreadyCheckedIn?: boolean;
  checkedInAt?: string | null;
  /** Echoed so the screen knows which row this answer is about. */
  applicationId?: string;
};

export type CorrectState = {
  error?: string;
  name?: string | null;
  applicationId?: string;
};

export async function findOverride(
  _previous: FindState,
  form: FormData,
): Promise<FindState> {
  const q = text(form, "q");
  if (q === "") {
    return { error: "Type a name or an email." };
  }

  let response: Response;
  try {
    response = await apiFetch(
      `/admin/check-in/override/find?q=${encodeURIComponent(q)}`,
    );
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (response.status === 401) {
    return { error: "Your session has ended. Sign in again." };
  }
  if (response.status === 403) {
    return { error: "You do not have checkin.override. Ask an admin." };
  }

  try {
    const body = (await response.json()) as {
      matches?: OverrideMatch[];
      error?: string;
    };
    if (!response.ok) {
      return { error: body.error ?? "That did not work." };
    }
    return { matches: body.matches ?? [] };
  } catch {
    return { error: "That did not work." };
  }
}

export async function admitWithoutCode(
  _previous: AdmitState,
  form: FormData,
): Promise<AdmitState> {
  const applicationId = text(form, "applicationId");
  const reason = text(form, "reason");

  if (applicationId === "") {
    return { error: "Find the person first." };
  }
  if (reason === "") {
    return { error: "An override needs a reason. Say what happened." };
  }

  let response: Response;
  try {
    response = await apiFetch("/admin/check-in/override/admit", {
      method: "POST",
      body: JSON.stringify({ applicationId, reason }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { error: "The API could not be reached. Try again.", applicationId };
  }

  if (response.status === 401) {
    return { error: "Your session has ended. Sign in again.", applicationId };
  }
  if (response.status === 403) {
    return { error: "You do not have checkin.override. Ask an admin.", applicationId };
  }

  // 200 (checkedIn / alreadyCheckedIn), 409 (notConfirmed), 404 and 400
  // all carry their own sentence — the same ones the scan gives, because
  // this is the same desk with the same queue.
  try {
    const body = (await response.json()) as {
      outcome?: string;
      message?: string;
      error?: string;
      name?: string | null;
      alreadyCheckedIn?: boolean;
      checkedInAt?: string | null;
    };

    if (!response.ok) {
      return {
        error: body.error ?? "That did not work.",
        name: body.name ?? null,
        applicationId,
      };
    }

    return {
      message: body.message,
      name: body.name ?? null,
      alreadyCheckedIn: body.alreadyCheckedIn ?? false,
      checkedInAt: body.checkedInAt ?? null,
      applicationId,
    };
  } catch {
    return { error: "That did not work.", applicationId };
  }
}

export async function correctName(
  _previous: CorrectState,
  form: FormData,
): Promise<CorrectState> {
  const applicationId = text(form, "applicationId");
  const firstName = text(form, "firstName");
  const lastName = text(form, "lastName");
  const reason = text(form, "reason");

  if (applicationId === "") {
    return { error: "Find the person first." };
  }
  if (firstName === "" || lastName === "") {
    return { error: "Both names are required." };
  }
  if (reason === "") {
    return { error: "An override needs a reason. Say what happened." };
  }

  let response: Response;
  try {
    response = await apiFetch("/admin/check-in/override/correct", {
      method: "POST",
      body: JSON.stringify({ applicationId, firstName, lastName, reason }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { error: "The API could not be reached. Try again.", applicationId };
  }

  if (response.status === 401) {
    return { error: "Your session has ended. Sign in again.", applicationId };
  }
  if (response.status === 403) {
    return { error: "You do not have checkin.override. Ask an admin.", applicationId };
  }

  try {
    const body = (await response.json()) as {
      corrected?: boolean;
      name?: string;
      error?: string;
    };

    if (!response.ok) {
      return { error: body.error ?? "That did not work.", applicationId };
    }

    return { name: body.name ?? null, applicationId };
  } catch {
    return { error: "That did not work.", applicationId };
  }
}
