import { apiFetch } from "@/lib/api";

/**
 * Volunteer shifts, as they come back from the API.
 *
 * Read-only on this side. Every write goes through actions.ts, so no
 * component in this folder ever holds the API's address or decides on its own
 * what a failure means.
 */

export type EventSummary = {
  id: string;
  slug: string;
  name: string;
  startsAt: string | null;
};

export type AssignmentView = {
  id: string;
  volunteerName: string;
  volunteerEmail: string | null;
  createdAt: string;
};

/**
 * One shift, with the names on it and the gap left.
 *
 * `gap` and `covered` arrive computed from `needed` and the assignments, so
 * they are shown exactly as given rather than recomputed here. A threshold
 * applied a second time on this side could disagree with the one the API used
 * to decide it, which is the kind of mismatch nobody notices until two
 * organizers are arguing over whether a shift is covered.
 */
export type ShiftView = {
  id: string;
  eventId: string;
  name: string;
  startsAt: string | null;
  endsAt: string | null;
  needed: number;
  assigned: number;
  gap: number;
  covered: boolean;
  assignments: AssignmentView[];
};

/** Everything the rota screen needs, in one response. */
export type VolunteersView = {
  events: EventSummary[];
  chosen: EventSummary | null;
  shifts: ShiftView[];
};

export type ViewRead =
  | { ok: true; view: VolunteersView }
  | { ok: false; status: number; error: string };

/**
 * What to say about a request that did not work.
 *
 * 403 names the permission, for the same reason the rest of this console
 * does: it turns "it doesn't work" into a request an admin can act on.
 */
function why(status: number, subject: string): string {
  if (status === 403) {
    return "You do not have volunteers.manage. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return `${subject} could not be loaded.`;
}

/** The chosen event's rota, and the events to choose from. */
export async function readView(eventId?: string): Promise<ViewRead> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/volunteer-shifts${eventId ? `?eventId=${encodeURIComponent(eventId)}` : ""}`,
    );
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: why(response.status, "Shifts") };
  }

  return { ok: true, view: (await response.json()) as VolunteersView };
}
