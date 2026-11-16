"use client";

import { useState, useTransition } from "react";
import { assignVolunteer, createShift, deleteShift, unassignVolunteer } from "./actions";
import { readable } from "../forms/[id]/when";
import styles from "./volunteers.module.css";

/**
 * The forms on the rota screen.
 *
 * Plain inline forms rather than dialogs: this screen is a rota the logistics
 * team edits a few times before the event, not a desk somebody operates
 * against a queue, so the dialog ceremony the inventory screen earns would be
 * chrome here. What matters is the failure path -- the API's sentence, shown
 * where the save happened -- which these keep.
 *
 * Times are `datetime-local` wall-clock fields in the event's zone, the same
 * convention as the form builder's deadline: an organizer typing Saturday 8am
 * means 8am in Baltimore, and the conversion to an instant happens in the
 * server action, not here.
 */

export function AddShiftForm({ eventId }: { eventId: string }) {
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  return (
    <form
      className={styles.form}
      action={(form: FormData) =>
        start(async () => {
          setError(null);
          const result = await createShift(eventId, {}, form);
          setError(result.error ?? null);
        })
      }
    >
      <label>
        Shift
        <input name="name" required maxLength={120} disabled={pending} />
      </label>
      <label>
        Starts
        <input name="startsAt" type="datetime-local" disabled={pending} />
      </label>
      <label>
        Ends
        <input name="endsAt" type="datetime-local" disabled={pending} />
      </label>
      <label>
        Needed
        <input name="needed" inputMode="numeric" defaultValue="1" disabled={pending} />
      </label>
      {error ? <p className={styles.error}>{error}</p> : null}
      <button type="submit" className="button primary" disabled={pending}>
        Add shift
      </button>
    </form>
  );
}

export function AssignForm({ shiftId }: { shiftId: string }) {
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  return (
    <form
      className={styles.assignForm}
      action={(form: FormData) =>
        start(async () => {
          setError(null);
          const result = await assignVolunteer(shiftId, {}, form);
          setError(result.error ?? null);
        })
      }
    >
      <label>
        Name
        <input name="volunteerName" required maxLength={160} disabled={pending} />
      </label>
      <label>
        Email (optional)
        <input name="volunteerEmail" type="email" maxLength={254} disabled={pending} />
      </label>
      {error ? <p className={styles.error}>{error}</p> : null}
      <button type="submit" className="button" disabled={pending}>
        Put on shift
      </button>
    </form>
  );
}

export function DeleteShiftButton({ id, name }: { id: string; name: string }) {
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  return (
    <span>
      <button
        type="button"
        className="button"
        disabled={pending}
        aria-label={`Remove the ${name} shift`}
        onClick={() =>
          start(async () => {
            setError(null);
            const result = await deleteShift(id);
            setError(result.error ?? null);
          })
        }
      >
        Remove
      </button>
      {error ? <span className={styles.error}>{error}</span> : null}
    </span>
  );
}

export function UnassignButton({
  shiftId,
  assignmentId,
  name,
}: {
  shiftId: string;
  assignmentId: string;
  name: string;
}) {
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  return (
    <span>
      <button
        type="button"
        className="button"
        disabled={pending}
        aria-label={`Take ${name} off this shift`}
        onClick={() =>
          start(async () => {
            setError(null);
            const result = await unassignVolunteer(shiftId, assignmentId);
            setError(result.error ?? null);
          })
        }
      >
        Take off
      </button>
      {error ? <span className={styles.error}>{error}</span> : null}
    </span>
  );
}

/** A wall-clock time in the event's zone, or the honest "not set yet". */
export function ShiftTime({ iso }: { iso: string | null }) {
  if (iso === null) {
    return <span className={styles.note}>Time to be decided</span>;
  }

  return <span>{readable(iso)}</span>;
}
