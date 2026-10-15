import { redirect } from "next/navigation";
import { currentPerson } from "@/lib/api";
import { CHECK_IN_DESK, isOn } from "@/lib/features";
import { Shell } from "../shell";
import { ScanForm } from "./form";
import { OverridePanel } from "./override-form";

/**
 * Check-in desk, for logistics and volunteers on event day.
 *
 * Two halves on one page because it is one desk, and which half
 * somebody sees is decided by which permission they hold:
 *
 * - checkin.scan gets the code box: the 12-char code from the
 *   hacker's /portal/check-in screen, typed or pasted here and
 *   redeemed through POST /admin/check-in/scan.
 * - checkin.override gets the override panel: finding somebody whose
 *   code will not scan, letting them in with a reason, and correcting
 *   a name the portal has locked. Deliberately a separate permission —
 *   the volunteer scanning badges and the person authorised to
 *   override are not always the same person.
 *
 * Refusals keep the API's rule: only confirmed may become checked
 * in, override or not. Anything else names an organizer rather than
 * offering a confirm-at-door button — the override is a door, not a
 * decision, and the API has no request shape that could make it one.
 */
export default async function CheckIn() {
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

  const canScan = person.permissions.has("checkin.scan");
  const canOverride = person.permissions.has("checkin.override");

  if (!canScan && !canOverride) {
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

  return (
    <Shell personId={person.personId}>
      <h1>Check-in</h1>

      {canScan ? (
        <>
          <p>
            Type the code from the hacker&apos;s portal. Check the name on the answer
            against the person in front of you.
          </p>
          <ScanForm />
        </>
      ) : null}

      {canOverride ? (
        <>
          <h2 style={{ marginTop: 32 }}>Override</h2>
          <p>
            For the code that will not scan and the name that will not change. Both take a
            reason, and both go into the audit log with your name against them.
          </p>
          <OverridePanel />
        </>
      ) : null}
    </Shell>
  );
}
