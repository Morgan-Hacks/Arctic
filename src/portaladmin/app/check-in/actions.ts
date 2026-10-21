"use server";

import { apiFetch } from "@/lib/api";

/**
 * The door's half of check-in, in the console.
 *
 * Two screens hitting the two halves of the desk — POST
 * /admin/check-in/scan for the code, and the /admin/check-in/override
 * routes for the person whose code will not scan. No camera yet: the
 * code is typed (or pasted) by hand, which is also the fallback
 * a cracked screen or bad light ends at anyway.
 *
 * The API's sentences are shown verbatim. They were written for
 * somebody standing in a doorway with a queue behind them, so this
 * file does not reword them and does not invent its own.
 */

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

  let response: Response;
  try {
    response = await apiFetch("/admin/check-in/scan", {
      method: "POST",
      body: JSON.stringify({ code }),
      headers: { "content-type": "application/json" },
    });
  } catch {
    return { error: "The API could not be reached. Try again.", code };
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
  event?: string | null;
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
