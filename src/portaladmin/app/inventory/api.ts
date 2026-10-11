import { apiFetch } from "@/lib/api";

/**
 * Inventory, as it comes back from the API.
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

/**
 * One item logistics is tracking, for the chosen event.
 *
 * `isLow` is computed by the API from the item's own `lowAt` threshold and its
 * current count, and is shown exactly as given rather than recomputed here.
 * A threshold applied a second time on this side could disagree with the one
 * the API used to decide it, which is the kind of mismatch nobody notices
 * until two organizers are arguing over whether sixty shirts counts as low.
 */
export type ItemView = {
  id: string;
  name: string;
  category: string | null;
  location: string | null;
  note: string | null;
  lowAt: number | null;
  count: number;
  isLow: boolean;
  /** Null until somebody adds one. The <img> this feeds stays ready for when it isn't. */
  imageUrl: string | null;
  updatedAt: string;
};

/**
 * One recorded change to an item's count.
 *
 * `balance` is the count immediately after this movement, not the count now
 * -- a later movement can move it again, and this is the figure a reader
 * checks the running arithmetic against.
 */
export type MovementView = {
  id: string;
  delta: number;
  balance: number;
  reason: string | null;
  actor: string | null;
  occurredAt: string;
};

/** Everything the screen needs to draw the list, in one response. */
export type InventoryView = {
  events: EventSummary[];
  chosen: EventSummary | null;
  items: ItemView[];
};

export type ViewRead =
  | { ok: true; view: InventoryView }
  | { ok: false; status: number; error: string };

export type MovementsRead =
  | { ok: true; movements: MovementView[] }
  | { ok: false; status: number; error: string };

/**
 * What to say about a request that did not work.
 *
 * 403 names the permission, for the same reason the rest of this console
 * does: it turns "it doesn't work" into a request an admin can act on.
 */
function why(status: number, subject: string): string {
  if (status === 403) {
    return "You do not have inventory.manage. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return `${subject} could not be loaded.`;
}

/** The chosen event's stock, and the events to choose from. */
export async function readView(eventId?: string): Promise<ViewRead> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/inventory${eventId ? `?eventId=${encodeURIComponent(eventId)}` : ""}`,
    );
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: why(response.status, "Inventory") };
  }

  return { ok: true, view: (await response.json()) as InventoryView };
}

/** One item's full movement history. */
export async function readMovements(id: string): Promise<MovementsRead> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/inventory/${encodeURIComponent(id)}/movements`);
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return {
      ok: false,
      status: response.status,
      error: response.status === 404 ? "That item could not be found." : why(response.status, "History"),
    };
  }

  const { movements } = (await response.json()) as { movements: MovementView[] };
  return { ok: true, movements };
}
