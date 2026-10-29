import { apiFetch } from "@/lib/api";

/**
 * Sponsors, as they come back from the API.
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

export type SponsorView = {
  id: string;
  eventId: string;
  name: string;
  tier: string;
  status: string;
  websiteUrl: string | null;
  contactName: string | null;
  contactEmail: string | null;
  /** Null where the reader may not see it, not where nothing was committed. */
  amountCents: number | null;
  createdAt: string;
  updatedAt: string;
};

export type AnalyticsBucket = { label: string; count: number };

export type SponsorReport = {
  eventId: string;
  totalApplicants: number;
  submittedApplications: number;
  accepted: number;
  confirmed: number;
  checkedIn: number;
  schoolCount: number;
  topSchools: AnalyticsBucket[];
  sponsorsByTier: { tier: string; count: number }[];
  demographics: {
    genderCollected: boolean;
    gender: AnalyticsBucket[];
    education: AnalyticsBucket[];
    experience: AnalyticsBucket[];
  } | null;
};

export type SponsorFinancials = {
  eventId: string;
  sponsorCount: number;
  committedCents: number;
  paidCents: number;
  byTier: { tier: string; count: number; committedCents: number; paidCents: number }[];
};

/** Everything the list screen needs, in one response. */
export type SponsorsView = {
  events: EventSummary[];
  chosen: EventSummary | null;
  sponsors: SponsorView[];
  canEdit: boolean;
  canViewFinancials: boolean;
};

export type SponsorsRead =
  | { ok: true; view: SponsorsView }
  | { ok: false; status: number; error: string };

export type ReportView = {
  events: EventSummary[];
  chosen: EventSummary | null;
  report: SponsorReport | null;
  canViewResponses: boolean;
  canViewFinancials: boolean;
  financials: SponsorFinancials | null;
};

export type ReportRead =
  | { ok: true; view: ReportView }
  | { ok: false; status: number; error: string };

/**
 * What to say about a request that did not work.
 *
 * 403 names the permission, for the same reason the rest of this console
 * does: it turns "it doesn't work" into a request an admin can act on.
 */
function why(status: number, subject: string): string {
  if (status === 403) {
    return "You do not have sponsors.view. Ask an admin.";
  }

  if (status === 401) {
    return "Your session has ended. Sign in again.";
  }

  return `${subject} could not be loaded.`;
}

/** The chosen event's sponsors, and the events to choose from. */
export async function readSponsors(eventId?: string): Promise<SponsorsRead> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/sponsors${eventId ? `?eventId=${encodeURIComponent(eventId)}` : ""}`,
    );
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: why(response.status, "Sponsors") };
  }

  return { ok: true, view: (await response.json()) as SponsorsView };
}

/** The sponsor-facing numbers for the chosen event, with the money kept apart. */
export async function readReport(eventId?: string): Promise<ReportRead> {
  let response: Response;
  try {
    response = await apiFetch(
      `/admin/sponsors/report${eventId ? `?eventId=${encodeURIComponent(eventId)}` : ""}`,
    );
  } catch {
    return { ok: false, status: 0, error: "The API could not be reached." };
  }

  if (!response.ok) {
    return { ok: false, status: response.status, error: why(response.status, "The sponsor report") };
  }

  return { ok: true, view: (await response.json()) as ReportView };
}

export const tierLabels: Record<string, string> = {
  platinum: "Platinum",
  gold: "Gold",
  silver: "Silver",
  bronze: "Bronze",
  community: "Community",
  in_kind: "In-kind",
};

export function tierLabel(tier: string): string {
  return tierLabels[tier] ?? tier;
}

/** Cents to the dollars-and-cents figure finance actually reads. */
export function money(cents: number): string {
  return new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" }).format(cents / 100);
}
