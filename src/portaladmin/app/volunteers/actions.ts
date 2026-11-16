"use server";

import { revalidatePath } from "next/cache";
import { apiFetch } from "@/lib/api";
import { fromLocalInput } from "../forms/[id]/when";

/**
 * Every write the rota screen makes.
 *
 * Reachable by anybody with a session, as every action in this console is.
 * That is not the gate -- the API refuses on `volunteers.manage` whoever asks
 * -- these only forward its refusal rather than deciding anything themselves.
 */

export type ShiftState = { error?: string };

/**
 * Reads the API's own refusal, or says something true when it did not answer.
 *
 * The API's sentence rather than one invented here, because the API is the
 * one that knows whether the name was blank, the crew was zero, or the shift
 * was gone. A second copy of that knowledge over here would drift the first
 * time somebody changed the rule on one side.
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

function text(form: FormData, field: string): string {
  const value = form.get(field);
  return typeof value === "string" ? value.trim() : "";
}

/**
 * Adds a shift to the chosen event.
 *
 * Times arrive as `datetime-local` wall-clock fields and leave as instants:
 * the event's zone, not the browser's, for the reason the form builder's
 * `when.ts` gives -- an organizer on Pacific time typing Saturday 8am means
 * Saturday 8am in Baltimore, and the flyer agrees with this screen only if
 * both say so. Blank stays blank: the name is agreed before the timetable is,
 * and demanding times up front would mean inventing them.
 */
export async function createShift(
  eventId: string,
  _previous: ShiftState,
  form: FormData,
): Promise<ShiftState> {
  const name = text(form, "name");
  if (!name) {
    return { error: "Give the shift a name." };
  }

  const rawNeeded = text(form, "needed");
  const needed = rawNeeded === "" ? 1 : Number(rawNeeded);
  if (!Number.isInteger(needed) || needed < 1) {
    return { error: "A shift needs at least one person." };
  }

  const startsAt = fromLocalInput(text(form, "startsAt"));
  const endsAt = fromLocalInput(text(form, "endsAt"));

  let response: Response;
  try {
    response = await apiFetch(`/admin/volunteer-shifts?eventId=${encodeURIComponent(eventId)}`, {
      method: "POST",
      body: JSON.stringify({ name, startsAt, endsAt, needed }),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That shift could not be added.") };
  }

  revalidatePath("/volunteers");
  revalidatePath("/");
  return {};
}

/** Removes a shift and the names on it. */
export async function deleteShift(id: string): Promise<ShiftState> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/volunteer-shifts/${encodeURIComponent(id)}`, {
      method: "DELETE",
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That shift could not be removed.") };
  }

  revalidatePath("/volunteers");
  revalidatePath("/");
  return {};
}

/**
 * Puts a name on a shift.
 *
 * Typed, not picked: the volunteer may not exist as a person row -- coverage
 * is planned from a sheet before the accounts exist -- so there is no roster
 * to pick from. What is refused here is an empty name, not an unknown one.
 */
export async function assignVolunteer(
  shiftId: string,
  _previous: ShiftState,
  form: FormData,
): Promise<ShiftState> {
  const volunteerName = text(form, "volunteerName");
  if (!volunteerName) {
    return { error: "Give a name to put on this shift." };
  }

  const rawEmail = text(form, "volunteerEmail");
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/volunteer-shifts/${encodeURIComponent(shiftId)}/assignments`,
      {
        method: "POST",
        body: JSON.stringify({
          volunteerName,
          volunteerEmail: rawEmail === "" ? null : rawEmail,
        }),
      },
    );
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That name could not be added.") };
  }

  revalidatePath("/volunteers");
  revalidatePath("/");
  return {};
}

/** Takes a name off a shift. */
export async function unassignVolunteer(
  shiftId: string,
  assignmentId: string,
): Promise<ShiftState> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/volunteer-shifts/${encodeURIComponent(shiftId)}/assignments/${encodeURIComponent(assignmentId)}`,
      { method: "DELETE" },
    );
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That name could not be removed.") };
  }

  revalidatePath("/volunteers");
  revalidatePath("/");
  return {};
}
