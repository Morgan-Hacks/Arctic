import { redirect } from "next/navigation";
import { currentPerson } from "@/lib/api";
import { CHECK_IN_DESK, isOn } from "@/lib/features";
import { Shell } from "../shell";
import { loadCheckInCounts } from "../d-day/actions";
import { searchRoster } from "./actions";
import { CheckedInCount } from "./count";
import { ScanForm } from "./form";
import { OverridePanel } from "./override-form";
import { Roster } from "./roster";
import styles from "./desk.module.css";

/**
 * Check-in desk, for logistics and volunteers on event day.
 *
 * Three things, because a door needs all three: a code redeemed through
 * POST /admin/check-in/scan — typed, pasted, or read off a QR symbol by the
 * camera — a searchable list of who is expected, and the checked-in count.
 *
 * Refusals keep the API's rule: only confirmed may become checked in.
 * Anything else names an organizer rather than offering a confirm-at-door
 * button, which would need a new API path. That holds for the roster too: it
 * tells a volunteer who somebody is and why they have no code, and it has no
 * buttons, because there is no route behind one.
 *
 * ## Two permissions, gated separately
 *
 * This is the subtle part of the screen and the reason the panels are
 * assembled here rather than inside one component.
 *
 * The scanner is `checkin.scan`. The roster and the count are both built out
 * of `GET /admin/applicants`, which is `applications.view`. A desk volunteer
 * is expected to hold the first and not the second — `sections.ts` says so in
 * as many words, which is why the D-Day children are filtered rather than
 * listed: "a volunteer holding only checkin.scan should see the desk and not
 * the numbers."
 *
 * So each panel is gated on its own permission and the screen degrades to
 * whichever half somebody is entitled to. A scanner with no roster is a
 * working desk; a roster with no scanner is a useful list for whoever is
 * walking the queue. The one thing that must not happen is the whole screen
 * refusing because half of it would have.
 *
 * Both halves also have to survive the refusal arriving anyway. `sections.ts`
 * is clear that hiding a link hides nothing and that atlas enforces on the
 * read: a permission can go while this tab is open, and the honest answer is
 * the API's sentence where a panel was, never an empty panel and never a
 * crash. That is what every `read.ok` check below is for.
 */
export const metadata = { title: "Check-in" };

export default async function CheckIn({
  searchParams,
}: {
  searchParams: Promise<{ eventId?: string }>;
}) {
  const { eventId } = await searchParams;
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  // Behind the flag until logistics signs off the flow. The nav hides the
  // tab while this is off, so a direct visit says so rather than 404ing.
  if (!isOn(CHECK_IN_DESK)) {
    return (
      <Shell personId={person.personId}>
        <h1>Check-in</h1>
        <div className="empty">Check-in is not enabled yet.</div>
      </Shell>
    );
  }

  const mayScan = person.permissions.has("checkin.scan");
  const mayRead = person.permissions.has("applications.view");
  const mayOverride = person.permissions.has("checkin.override");

  // Neither half. The desk's own permission is the one named, because it is
  // the one somebody sent to this screen is missing.
  if (!mayScan && !mayRead && !mayOverride) {
    return (
      <Shell personId={person.personId}>
        <h1>Check-in</h1>
        <div className="empty">
          You do not have <code>checkin.scan</code> or <code>checkin.override</code>. Ask an
          admin.
        </div>
      </Shell>
    );
  }

  // Together, because the page needs both to draw itself and two waterfalled
  // reads would be a second of a volunteer's morning for no benefit. Not
  // asked for at all without the permission: a read made to be refused is a
  // query spent to learn what the session already said.
  const [roster, counts] = mayRead
    ? await Promise.all([searchRoster(eventId, ""), loadCheckInCounts(eventId)])
    : [null, null];

  const event = roster?.ok ? roster.view.chosen : null;

  return (
    <Shell personId={person.personId}>
      <div className={styles.page}>
        <div className={styles.head}>
          <div>
            <h1>Check-in</h1>
            <p className={styles.lede}>
              Scan or type the code from the hacker&apos;s portal. Check the
              name on the answer against the person in front of you.
            </p>
            {event ? <p className={styles.event}>{event.name}</p> : null}
          </div>

          {counts ? (
            <CheckedInCount initial={counts} eventId={eventId} />
          ) : null}
        </div>

        {mayScan || mayRead ? (
        <div className={styles.panels}>
          {mayScan ? (
            <ScanForm hasRoster={mayRead} />
          ) : (
            <div className="empty">
              {/* A roster without a scanner. Worth saying rather than
                  leaving a gap where the box is on everybody else's
                  screen. */}
              You do not have <code>checkin.scan</code>, so you cannot redeem
              codes here. The list is still yours to search.
            </div>
          )}

          {mayRead ? <RosterPanel read={roster} /> : <NoRoster />}
        </div>
        ) : null}

        {mayOverride ? (
          <section className={styles.override}>
            <h2>Override</h2>
            <p>
              For the code that will not scan and the name that will not change. Both take a
              reason, and both go into the audit log with your name against them.
            </p>
            <OverridePanel />
          </section>
        ) : null}
      </div>
    </Shell>
  );
}

/**
 * The roster, or why there is not one.
 *
 * Three different sentences, because "you cannot see this", "there is nothing
 * to see" and "the read did not work" are three different facts and only one
 * of them is true at a time. The API's own wording for the refusal, which for
 * a 403 names the permission an admin can grant.
 */
function RosterPanel({ read }: { read: Awaited<ReturnType<typeof searchRoster>> | null }) {
  if (read === null || !read.ok) {
    return (
      <div className="empty">
        {read?.error ?? "The door list could not be loaded."}
      </div>
    );
  }

  const { chosen } = read.view;

  if (!chosen) {
    return (
      <div className="empty">
        No event to check anybody in to yet. Create one under Events.
      </div>
    );
  }

  return (
    <Roster
      initial={read.view}
      // Bound to the event the server chose, not to whatever the browser
      // believed it was looking at. A search sent with the wrong event is a
      // search that confidently answers about the wrong people.
      search={searchRoster.bind(null, chosen.id)}
    />
  );
}

/**
 * What a desk volunteer sees where the roster would be.
 *
 * Named rather than silent. A screen that is quietly missing a panel
 * everybody else describes is a screen somebody reports as broken, and the
 * sentence turns that into a request an admin can act on.
 */
function NoRoster() {
  return (
    <div className="empty">
      The door list and the checked-in count need <code>applications.view</code>
      , which you do not have. Scanning works without it. Ask an admin if you
      need to look people up by name.
    </div>
  );
}
