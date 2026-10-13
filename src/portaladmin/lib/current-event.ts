import { cache } from "react";
import { apiFetch } from "./api";

/**
 * The event being run, as the console's own list orders them.
 *
 * `GET /admin/events` answers newest first, and the newest is the one being
 * run -- the same assumption every screen's picker makes when nothing in the
 * URL says otherwise.
 *
 * Memoized per request like `currentPerson`, because the shell reads it to
 * decide what the bar shows and a screen under it may read it again. Two
 * requests for one list that cannot have changed between them is a waterfall
 * for nothing.
 */
export type CurrentEvent = {
  id: string;
  name: string;
  startsAt: string | null;
};

export const currentEvent = cache(async function currentEvent(): Promise<CurrentEvent | null> {
  try {
    const response = await apiFetch("/admin/events", {
      signal: AbortSignal.timeout(10000),
    });

    if (!response.ok) {
      return null;
    }

    const body = await response.json();
    const first = body?.events?.[0];

    return first?.id
      ? {
          id: first.id,
          name: first.name ?? "Untitled event",
          startsAt: first.startsAt ?? null,
        }
      : null;
  } catch {
    // Null rather than a throw. This decides whether a tab carries a padlock,
    // and a console that will not render because the events list was slow is
    // a worse outcome than a tab without one.
    return null;
  }
});
