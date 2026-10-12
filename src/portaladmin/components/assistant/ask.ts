"use server";

import { apiFetch } from "@/lib/api";

/**
 * Asking the API what a question means.
 *
 * What comes back is a plan -- a screen and the filters to put on it -- and
 * never a row. The screen is then fetched the way it always was, by the page
 * the plan names, as the person asking. So nothing here needs to know who may
 * see what: this call cannot return an applicant, and the page that can is
 * the one that already refuses when it should.
 */

export type AskedPlan = {
  view: "applicants" | "count" | "analysis";
  statuses: string[];
  countOf: string | null;
  search: string | null;
  explain: string;
};

export type AnalysisRow = { label: string; count: number; share: number };

/**
 * A figure, or a reading over several.
 *
 * `basis` travels with every reading rather than sitting in a tooltip,
 * because each one divides one set of applicants by another and which went
 * into the denominator is a choice. A rate whose basis is unstated is a rate
 * two people read two ways.
 */
export type Answer =
  | { value: number; headline?: never; basis?: never; rows?: never; empty?: never }
  | { value?: never; headline: string; basis: string; rows: AnalysisRow[]; empty?: never }
  /** The reading exists but there is not enough yet to compute it. */
  | { empty: true; value?: never; headline?: never; basis?: never; rows?: never };

export type AskResult =
  | { plan: AskedPlan; answer: Answer | null; refusal?: never; error?: never }
  | { plan?: never; answer?: never; refusal: string; error?: never }
  | { plan?: never; answer?: never; refusal?: never; error: string };

export async function ask(question: string, eventId?: string): Promise<AskResult> {
  let response: Response;
  try {
    response = await apiFetch("/admin/assistant/ask", {
      method: "POST",
      body: JSON.stringify({ question, eventId }),
      signal: AbortSignal.timeout(15000),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (response.status === 403) {
    return { error: "You do not have permission to view applicants. Ask an admin for applications.view access." };
  }

  if (response.status === 401) {
    return { error: "Your session has ended. Sign in again." };
  }

  let body: { plan?: AskedPlan; answer?: Answer | null; refusal?: string; error?: string };
  try {
    body = await response.json();
  } catch {
    return { error: "That question could not be read." };
  }

  if (!response.ok) {
    return { error: body.error ?? "That question could not be read." };
  }

  // A refusal is a 200 with a sentence: the question was well formed and this
  // could not place it, which is an answer about the question rather than a
  // fault in the request.
  if (body.refusal) {
    return { refusal: body.refusal };
  }

  return body.plan
    ? { plan: body.plan, answer: body.answer ?? null }
    : { error: "That question could not be read." };
}
