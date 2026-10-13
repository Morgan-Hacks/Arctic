"use server";

import { apiFetch } from "@/lib/api";

/**
 * The checked-in count, asked for on its own clock.
 *
 * Separate from `loadApplicantAnalytics` for a reason that is about cost
 * rather than freshness. That read scans the whole cohort and, for anybody
 * who may read answers, parses every response in it as well — a fair price
 * once a minute and a silly one six times a minute, which is why the board's
 * other numbers keep their minute. This is the same figure the applicants
 * list already computes as `counts`, and it costs one grouped pass.
 *
 * `limit=1` is not a page anybody reads. The row comes back because the route
 * always pages and is thrown away here; what is asked for is the counts strip
 * that rides along with it. There is no counts-only route, and asking this one
 * for a single row is cheaper than adding a second route to keep honest.
 *
 * Same permission as the screen it appears on — `applications.view`, which is
 * what the board is built out of — so this adds nobody to the audience of a
 * number they could already read.
 */
export type CheckInCountsResult =
  | { counts: Record<string, number>; updatedAt: string; error?: never }
  | { counts?: never; updatedAt?: never; error: string };

export async function loadCheckInCounts(
  eventId?: string,
): Promise<CheckInCountsResult> {
  try {
    const params = new URLSearchParams({ limit: "1" });
    if (eventId) params.set("eventId", eventId);

    const response = await apiFetch(`/admin/applicants?${params}`, {
      signal: AbortSignal.timeout(10_000),
    });

    if (!response.ok) {
      return {
        error: response.status === 403
          ? "You do not have applications.view. Ask an admin."
          : response.status === 401
            ? "Your session has ended. Sign in again."
            : "The check-in count could not be loaded.",
      };
    }

    const body = (await response.json()) as { counts?: Record<string, number> };
    return { counts: body.counts ?? {}, updatedAt: new Date().toISOString() };
  } catch {
    return { error: "The check-in count could not be loaded." };
  }
}
