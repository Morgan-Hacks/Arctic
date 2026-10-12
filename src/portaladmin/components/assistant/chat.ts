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
  | { threadId: string; answer: string; basis: string[]; error?: never }
  | { threadId?: never; answer?: never; basis?: never; error: string };

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

export async function say(
  question: string,
  eventId: string,
  threadId?: string,
): Promise<Said> {
  let response: Response;
  try {
    response = await apiFetch("/admin/assistant/chat", {
      method: "POST",
      body: JSON.stringify({ question, eventId, threadId }),
      signal: AbortSignal.timeout(PATIENCE),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  const refused = unreachable(response.status);
  if (refused) {
    return { error: refused };
  }

  let body: { threadId?: string; answer?: string; basis?: string[]; error?: string };
  try {
    body = await response.json();
  } catch {
    return { error: "That answer could not be read." };
  }

  if (!response.ok) {
    return { error: body.error ?? "That question could not be read." };
  }

  return body.threadId && body.answer
    ? { threadId: body.threadId, answer: body.answer, basis: body.basis ?? [] }
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

export type Here = { eventId: string | null; name: string | null };

/**
 * Which event the panel asks about when the page has not said.
 *
 * The same event the applicants screen defaults to, read the same way, rather
 * than a choice made here. The panel appears on every screen and most of them
 * have no event in their URL, so without this it would open asking a question
 * before it could answer one -- and picking whichever event came back first
 * would make "how many confirmed" a wrong answer that looks right.
 */
export async function here(): Promise<Here> {
  try {
    const response = await apiFetch("/admin/applicants", {
      signal: AbortSignal.timeout(10000),
    });

    if (!response.ok) {
      return { eventId: null, name: null };
    }

    const body = await response.json();
    const chosen = body?.chosen ?? null;

    return chosen?.id
      ? { eventId: chosen.id, name: chosen.name ?? null }
      : { eventId: null, name: null };
  } catch {
    return { eventId: null, name: null };
  }
}
