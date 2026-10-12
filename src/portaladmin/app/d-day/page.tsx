import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { loadApplicantAnalytics } from "../home-actions";
import { LiveBoard } from "./live";

/**
 * The screen somebody leaves up at the organizer desk, on the day.
 *
 * Named for the day rather than for the shape of it. This is where the things
 * that only matter once doors open collect -- the live counts now, check-in
 * and whatever the desk turns out to need beside it later -- and keeping that
 * boundary in the name is what stops it becoming the console's front page by
 * accretion. Before the day there is nothing here worth looking at, and
 * nothing should send anybody to it.
 *
 * Deliberately not the home screen. Home is read by one person sitting down
 * to work and answers "what should I do next"; this is read from across a
 * room by whoever happens to look up, and answers "where are we". That makes
 * the numbers large, the panels few, and the refresh automatic.
 *
 * Composed from the analytics endpoint the home screen already uses rather
 * than new measurement, which also makes it a fair test of whether that
 * endpoint answers fast enough to be polled.
 *
 * Nothing here is a control. The endpoint is behind `applications.view`, and
 * a person without it gets the refusal that loader already writes.
 */
export const metadata = { title: "D-Day" };

export default async function DDay({
  searchParams,
}: {
  searchParams: Promise<{ eventId?: string }>;
}) {
  const { eventId } = await searchParams;
  const { person, data: initial } = await readPageData(() => loadApplicantAnalytics(eventId));

  return (
    <Shell personId={person.personId}>
      <LiveBoard initial={initial} eventId={eventId} />
    </Shell>
  );
}
