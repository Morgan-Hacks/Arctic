"use server";

import { revalidatePath } from "next/cache";
import { apiFetch } from "@/lib/api";
import { readMovements, type MovementView } from "./api";

/**
 * Every write this screen makes.
 *
 * Reachable by anybody with a session, as every action in this console is.
 * That is not the gate -- the API refuses on `inventory.manage` whoever asks
 * -- these only forward its refusal rather than deciding anything themselves.
 */

/** What a form or a dialog got back. An empty object is "nothing went wrong". */
export type MovementState = { error?: string };
export type ItemState = { error?: string };

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

/**
 * Reads the API's own refusal, or says something true when it did not answer.
 *
 * The API's sentence rather than one invented here, because the API is the
 * one that knows what is actually on hand. A second copy of that knowledge
 * over here would drift the first time somebody changed the rule on one side.
 */
async function refusal(response: Response, fallback: string): Promise<string> {
  if (response.status === 401) {
    return "Your session has ended. Sign in again.";
  }

  try {
    const { error } = (await response.json()) as { error?: string };
    return error ?? fallback;
  } catch {
    return fallback;
  }
}

/**
 * Adds or removes stock, in one movement.
 *
 * Called directly by the +1 / -1 buttons (delta of exactly 1, no reason) and
 * by the adjust dialog for everything larger. Both go through the same
 * endpoint and the same failure path, because the rule being enforced --
 * cannot take out more than is on hand -- does not change with the size of
 * the request, and a 400 here already names what is actually available. It
 * is shown as the API wrote it, never replaced with a guess of our own.
 */
export async function recordMovement(
  id: string,
  delta: number,
  reason: string | null,
): Promise<MovementState> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/inventory/${encodeURIComponent(id)}/movement`, {
      method: "POST",
      // A string body sets its own content-type on the way through apiFetch;
      // see the comment on apiFetch itself.
      body: JSON.stringify({ delta, reason }),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (response.status === 403) {
    return { error: "You do not have inventory.manage. Ask an admin." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That count could not be changed.") };
  }

  revalidatePath("/inventory");
  return {};
}

/**
 * The +1 / -1 buttons.
 *
 * Each is its own one-field form -- see Bump in item-card.tsx -- so a
 * row's failure (taking out more than is on hand) reports against the
 * button that was pressed and never against whichever item happened to load
 * first on the page.
 */
export async function bump(_previous: MovementState, form: FormData): Promise<MovementState> {
  const id = text(form, "id");
  const delta = Number(text(form, "delta"));

  if (!Number.isInteger(delta) || delta === 0) {
    return { error: "That is not a step this button sends." };
  }

  return recordMovement(id, delta, null);
}

export type NewItem = {
  name: string;
  category: string | null;
  location: string | null;
  note: string | null;
  lowAt: number | null;
  count: number;
};

/**
 * Starts an item at whatever is already on hand.
 *
 * The event is a query parameter rather than a body field, matching how
 * /admin/forms is scoped to an event -- the contract for this endpoint does
 * not spell out how a new item gets attached to an event, and that is the
 * one other place in this console with the same shape.
 */
export async function createItem(eventId: string, input: NewItem): Promise<ItemState> {
  const name = input.name.trim();
  if (!name) {
    return { error: "Give the item a name." };
  }

  let response: Response;
  try {
    response = await apiFetch(`/admin/inventory?eventId=${encodeURIComponent(eventId)}`, {
      method: "POST",
      body: JSON.stringify({ ...input, name }),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (response.status === 403) {
    return { error: "You do not have inventory.manage. Ask an admin." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That item could not be added.") };
  }

  revalidatePath("/inventory");
  return {};
}

export type ItemEdits = {
  name: string;
  category: string | null;
  location: string | null;
  note: string | null;
  lowAt: number | null;
};

/**
 * Saves an item's details. Never its count -- that only ever moves through
 * recordMovement, which is the one place a change to it is also a row in the
 * history.
 */
export async function updateItem(id: string, input: ItemEdits): Promise<ItemState> {
  const name = input.name.trim();
  if (!name) {
    return { error: "Give the item a name." };
  }

  let response: Response;
  try {
    response = await apiFetch(`/admin/inventory/${encodeURIComponent(id)}`, {
      method: "PATCH",
      body: JSON.stringify({ ...input, name }),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (response.status === 403) {
    return { error: "You do not have inventory.manage. Ask an admin." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That item could not be saved.") };
  }

  revalidatePath("/inventory");
  return {};
}

export async function deleteItem(id: string): Promise<ItemState> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/inventory/${encodeURIComponent(id)}`, { method: "DELETE" });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (response.status === 403) {
    return { error: "You do not have inventory.manage. Ask an admin." };
  }

  // 204 carries no body. response.ok covers it without trying to read one.
  if (!response.ok) {
    return { error: await refusal(response, "That item could not be deleted.") };
  }

  revalidatePath("/inventory");
  return {};
}

export type MovementsResult =
  | { ok: true; movements: MovementView[] }
  | { ok: false; error: string };

/**
 * One item's history, loaded on demand from the dialog rather than with the
 * list. A history nobody opens is a round trip nobody needed.
 */
export async function loadMovements(id: string): Promise<MovementsResult> {
  const read = await readMovements(id);
  return read.ok ? { ok: true, movements: read.movements } : { ok: false, error: read.error };
}
