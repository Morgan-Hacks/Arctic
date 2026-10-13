"use server";

import { apiFetch } from "@/lib/api";

/**
 * Talking to the assistant.
 *
 * Nothing here decides what anybody may see. The API picks which reads to
 * make from the asking person's own permissions, before any of them run, and
 * a refusal comes back as part of the answer naming the permission it wanted.
 * So this file carries sentences, not rows, and has no judgement to make.
 */

export type Speaker = "person" | "assistant";

/**
 * One thing said.
 *
 * `basis` is the list of reads an answer was built from, by name. It travels
 * with the turn rather than sitting in a log, because the difference between
 * a figure that came out of the funnel read and one the model recalled is the
 * difference between an answer and a guess -- and only one of them is worth
 * repeating to a sponsor.
 */
export type Turn = {
  speaker: Speaker;
  body: string;
  basis: string[];
  at: string;
};

export type ThreadSummary = {
  id: string;
  title: string;
  eventId: string | null;
  lastAt: string;
};

export type ThreadDetail = {
  id: string;
  title: string;
  eventId: string | null;
  turns: Turn[];
};

export type Said =
  | {
      threadId: string;
      answer: string;
      basis: string[];
      eventName: string;
      error?: never;
    }
  | {
      threadId?: never;
      answer?: never;
      basis?: never;
      eventName?: never;
      error: string;
    };

/** How long to wait. The agent may make several reads before it answers. */
const PATIENCE = 60000;

function unreachable(status: number): string | null {
  if (status === 403) {
    return "You do not have permission to view applicants. Ask an admin for applications.view access.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return null;
}

/**
 * @param screen Which console screen the question was asked from, as one of
 * the names in `screen.ts`. A hint about what an unspecific question means,
 * never a permission -- the API matches it against its own list and every read
 * still runs against the asking person's own permissions.
 */
export async function say(
  question: string,
  eventId: string,
  threadId?: string,
  screen?: string,
): Promise<Said> {
  let response: Response;
  try {
    response = await apiFetch("/admin/assistant/chat", {
      method: "POST",
      body: JSON.stringify({ question, eventId, threadId, screen }),
      signal: AbortSignal.timeout(PATIENCE),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  const refused = unreachable(response.status);
  if (refused) {
    return { error: refused };
  }

  let body: {
    threadId?: string;
    answer?: string;
    basis?: string[];
    eventName?: string;
    error?: string;
  };
  try {
    body = await response.json();
  } catch {
    return { error: "That answer could not be read." };
  }

  if (!response.ok) {
    return { error: body.error ?? "That question could not be read." };
  }

  return body.threadId && body.answer
    ? {
        threadId: body.threadId,
        answer: body.answer,
        basis: body.basis ?? [],

        // Which event the API actually answered about, rather than the one
        // this side believed it had asked about. They should be the same; the
        // whole reason to carry it is the case where they are not.
        eventName: body.eventName ?? "",
      }
    : { error: "That answer came back empty." };
}

export async function threads(): Promise<{ threads: ThreadSummary[]; keptForDays: number }> {
  try {
    const response = await apiFetch("/admin/assistant/threads", {
      signal: AbortSignal.timeout(10000),
    });

    if (!response.ok) {
      return { threads: [], keptForDays: 0 };
    }

    const body = await response.json();
    return { threads: body.threads ?? [], keptForDays: body.keptForDays ?? 0 };
  } catch {
    // An empty list rather than an error. The panel's job is to take the next
    // question; failing to list old ones should not stop it.
    return { threads: [], keptForDays: 0 };
  }
}

export async function thread(id: string): Promise<ThreadDetail | null> {
  try {
    const response = await apiFetch(`/admin/assistant/threads/${id}`, {
      signal: AbortSignal.timeout(10000),
    });

    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

export async function forget(id: string): Promise<void> {
  try {
    await apiFetch(`/admin/assistant/threads/${id}`, {
      method: "DELETE",
      signal: AbortSignal.timeout(10000),
    });
  } catch {
    // Nothing to tell the caller. The panel drops the thread from its list
    // either way, and a delete that did not land leaves it there to retry.
  }
}

export type EventChoice = { id: string; name: string };

/** The events somebody may ask about, and the one the console would default to. */
export type Events = { events: EventChoice[]; chosen: EventChoice | null };

/**
 * Which events there are to ask about.
 *
 * Read from the applicants endpoint rather than from an endpoint of its own,
 * because that is the list the console's own pickers are drawn from and a
 * second source would eventually disagree with it about which event is
 * current.
 *
 * The panel asks once, when it opens, and the chosen event is fixed for a
 * conversation. An event is the one premise every figure carries and nothing
 * in a sentence would reveal if it were wrong, so it is picked deliberately
 * rather than inferred from whatever screen somebody happened to be on.
 */
export async function events(): Promise<Events> {
  try {
    const response = await apiFetch("/admin/applicants", {
      signal: AbortSignal.timeout(10000),
    });

    if (!response.ok) {
      return { events: [], chosen: null };
    }

    const body = await response.json();

    const all: EventChoice[] = (body?.events ?? [])
      .filter((one: { id?: string }) => Boolean(one?.id))
      .map((one: { id: string; name?: string }) => ({
        id: one.id,
        name: one.name ?? "Untitled event",
      }));

    const chosen = body?.chosen?.id
      ? all.find((one) => one.id === body.chosen.id) ?? null
      : null;

    return { events: all, chosen: chosen ?? all[0] ?? null };
  } catch {
    return { events: [], chosen: null };
  }
}
