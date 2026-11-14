import { EmptyState } from "@/components/ui/empty-state";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { EventPicker } from "./event-picker";
import {
  AddShiftForm,
  AssignForm,
  DeleteShiftButton,
  ShiftTime,
  UnassignButton,
} from "./volunteer-forms";
import { readView } from "./api";
import styles from "./volunteers.module.css";

/**
 * Who is covering which volunteer shift, and where the gaps are.
 *
 * A rota, not a recruitment tool: the names on a shift are what an organizer
 * typed in from a sheet, because a volunteer may have no application and no
 * identity row for a form to attach a response to. The portal only ever reads
 * what was entered -- which is why this screen has no audience settings and
 * no sign-in links, only names and the gap each shift still has.
 *
 * One question per shift, answered at a glance: how many more. A shift whose
 * gap is zero reads covered; anything else names the number still missing.
 */
export default async function Volunteers({
  searchParams,
}: {
  searchParams: Promise<{ eventId?: string }>;
}) {
  const { eventId } = await searchParams;
  const { person, data: read } = await readPageData(() => readView(eventId));

  if (!read.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Volunteers</h1>
        <div className="empty">{read.error}</div>
      </Shell>
    );
  }

  const { events, chosen, shifts } = read.view;

  if (!chosen) {
    return (
      <Shell personId={person.personId}>
        <h1>Volunteers</h1>
        <EmptyState
          variant="data"
          size="page"
          title="No events yet"
          description="Create an event under Events. Shifts are planned per event."
        />
      </Shell>
    );
  }

  const open = shifts.filter((shift) => !shift.covered).length;

  return (
    <Shell personId={person.personId}>
      <div className={styles.page}>
        <div className="page-head">
          <div>
            <h1>Volunteers</h1>
            <p className="lede">
              Who is covering which shift, and where the gaps are.
            </p>
          </div>
        </div>

        <EventPicker events={events} chosen={chosen} />

        {shifts.length === 0 ? (
          <EmptyState
            variant="data"
            size="page"
            title="No shifts yet"
            description="Add the first shift to start planning who covers the event."
          />
        ) : (
          <>
            <p className={styles.note} role="status">
              {open === 0
                ? "Every shift is covered."
                : `${open} of ${shifts.length} ${shifts.length === 1 ? "shift" : "shifts"} still ${open === 1 ? "has" : "have"} a gap.`}
            </p>
            <ul className={styles.list} aria-label="Volunteer shifts">
              {shifts.map((shift) => (
                <li key={shift.id} className={styles.card}>
                  <h2>{shift.name}</h2>
                  <p>
                    <ShiftTime iso={shift.startsAt} />
                    {" — "}
                    <ShiftTime iso={shift.endsAt} />
                  </p>
                  <p>
                    <span
                      className={styles.coverage}
                      data-gap={shift.covered ? "covered" : "open"}
                    >
                      {shift.covered
                        ? `Covered — ${shift.assigned} of ${shift.needed}`
                        : `${shift.gap} still needed — ${shift.assigned} of ${shift.needed}`}
                    </span>
                  </p>
                  {shift.assignments.length === 0 ? (
                    <p className={styles.note}>Nobody on this shift yet.</p>
                  ) : (
                    <ul className={styles.names}>
                      {shift.assignments.map((assignment) => (
                        <li key={assignment.id}>
                          <span>
                            {assignment.volunteerName}
                            {assignment.volunteerEmail ? (
                              <span className={styles.note}>
                                {" "}
                                · {assignment.volunteerEmail}
                              </span>
                            ) : null}
                          </span>
                          <UnassignButton
                            shiftId={shift.id}
                            assignmentId={assignment.id}
                            name={assignment.volunteerName}
                          />
                        </li>
                      ))}
                    </ul>
                  )}
                  <AssignForm shiftId={shift.id} />
                  <p>
                    <DeleteShiftButton id={shift.id} name={shift.name} />
                  </p>
                </li>
              ))}
            </ul>
          </>
        )}

        <section className={styles.card} aria-labelledby="add-shift">
          <h2 id="add-shift">Add a shift</h2>
          <p>
            Times are in the event&apos;s zone. Leave them blank when only the
            name is agreed so far.
          </p>
          <AddShiftForm eventId={chosen.id} />
        </section>
      </div>
    </Shell>
  );
}
