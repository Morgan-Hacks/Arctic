"use server";

import { revalidatePath } from "next/cache";
import { apiFetch } from "@/lib/api";

/**
 * Every write the sponsors screen makes.
 *
 * Reachable by anybody with a session, as every action in this console is.
 * That is not the gate -- the API refuses on `sponsors.edit` (and on
 * `sponsors.view_financials` for amounts) whoever asks -- these only forward
 * its refusal rather than deciding anything themselves.
 */

export type SponsorState = { error?: string };

/**
 * Reads the API's own refusal, or says something true when it did not answer.
 *
 * The API's sentence rather than one invented here, because the API is the
 * one that knows whether the tier was wrong, the amount was negative, or the
 * name was taken. A second copy of that knowledge over here would drift the
 * first time somebody changed the rule on one side.
 */
async function refusal(response: Response, fallback: string): Promise<string> {
  if (response.status === 401) {
    return "Your session has ended. Sign in again.";
  }

  if (response.status === 403) {
    try {
      const { error } = (await response.json()) as { error?: string };
      return error ?? fallback;
    } catch {
      return fallback;
    }
  }

  try {
    const { error } = (await response.json()) as { error?: string };
    return error ?? fallback;
  } catch {
    return fallback;
  }
}

function optional(form: FormData, field: string): string | null {
  const value = form.get(field);
  if (typeof value !== "string") return null;
  const trimmed = value.trim();
  return trimmed === "" ? null : trimmed;
}

/**
 * Adds a sponsor to the chosen event.
 *
 * Amounts go through untouched: the API refuses them without
 * `sponsors.view_financials` rather than dropping them, so a finance figure
 * somebody typed never reads as saved when it was not.
 */
export async function createSponsor(
  eventId: string,
  _previous: SponsorState,
  form: FormData,
): Promise<SponsorState> {
  const name = (form.get("name") as string | null)?.trim() ?? "";
  if (!name) {
    return { error: "Give the sponsor a name." };
  }

  const rawAmount = optional(form, "amountCents");
  let amountCents: number | null = null;
  if (rawAmount !== null) {
    const parsed = Number(rawAmount);
    if (!Number.isInteger(parsed)) {
      return { error: "A commitment is a whole number of cents." };
    }
    amountCents = parsed;
  }

  let response: Response;
  try {
    response = await apiFetch(`/admin/sponsors?eventId=${encodeURIComponent(eventId)}`, {
      method: "POST",
      body: JSON.stringify({
        name,
        tier: form.get("tier"),
        status: form.get("status") || "prospective",
        websiteUrl: optional(form, "websiteUrl"),
        contactName: optional(form, "contactName"),
        contactEmail: optional(form, "contactEmail"),
        amountCents,
      }),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That sponsor could not be added.") };
  }

  revalidatePath("/sponsors");
  return {};
}

/** Saves a sponsor's details. Amounts follow the same rule as the create. */
export async function updateSponsor(
  id: string,
  _previous: SponsorState,
  form: FormData,
): Promise<SponsorState> {
  const rawName = form.get("name");
  const rawTier = form.get("tier");
  const rawStatus = form.get("status");

  const body: Record<string, unknown> = {};
  if (typeof rawName === "string" && rawName.trim() !== "") {
    body.name = rawName.trim();
  }
  if (typeof rawTier === "string" && rawTier !== "") {
    body.tier = rawTier;
  }
  if (typeof rawStatus === "string" && rawStatus !== "") {
    body.status = rawStatus;
  }
  for (const field of ["websiteUrl", "contactName", "contactEmail"] as const) {
    if (form.has(field)) {
      body[field] = optional(form, field);
    }
  }
  if (form.has("amountCents")) {
    const rawAmount = optional(form, "amountCents");
    if (rawAmount === null) {
      body.amountCents = null;
    } else {
      const parsed = Number(rawAmount);
      if (!Number.isInteger(parsed)) {
        return { error: "A commitment is a whole number of cents." };
      }
      body.amountCents = parsed;
    }
  }

  let response: Response;
  try {
    response = await apiFetch(`/admin/sponsors/${encodeURIComponent(id)}`, {
      method: "PATCH",
      body: JSON.stringify(body),
    });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  if (!response.ok) {
    return { error: await refusal(response, "That sponsor could not be saved.") };
  }

  revalidatePath("/sponsors");
  return {};
}

export async function deleteSponsor(id: string): Promise<SponsorState> {
  let response: Response;
  try {
    response = await apiFetch(`/admin/sponsors/${encodeURIComponent(id)}`, { method: "DELETE" });
  } catch {
    return { error: "The API could not be reached. Try again." };
  }

  // 204 carries no body. response.ok covers it without trying to read one.
  if (!response.ok) {
    return { error: await refusal(response, "That sponsor could not be removed.") };
  }

  revalidatePath("/sponsors");
  return {};
}
