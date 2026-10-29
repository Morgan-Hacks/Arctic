"use client";

import { useState, useTransition } from "react";
import { createSponsor, deleteSponsor, updateSponsor } from "./actions";
import type { SponsorView } from "./api";
import styles from "./sponsors.module.css";

/**
 * The forms on the sponsors screen.
 *
 * Plain inline forms rather than dialogs: this screen is a table the
 * sponsorship team edits a few times a season, not a desk somebody operates
 * against a queue, so the dialog ceremony the inventory screen earns would
 * be chrome here. What matters is the failure path -- the API's sentence,
 * shown where the save happened -- which these keep.
 */

const tiers = ["platinum", "gold", "silver", "bronze", "community", "in_kind"];
const statuses = ["prospective", "committed", "paid", "declined"];

export function AddSponsorForm({
  eventId,
  canViewFinancials,
}: {
  eventId: string;
  canViewFinancials: boolean;
}) {
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  return (
    <form
      className={styles.form}
      action={(form: FormData) =>
        start(async () => {
          setError(null);
          const result = await createSponsor(eventId, {}, form);
          setError(result.error ?? null);
        })
      }
    >
      <label>
        Name
        <input name="name" required maxLength={200} disabled={pending} />
      </label>
      <label>
        Tier
        <select name="tier" defaultValue="bronze" disabled={pending}>
          {tiers.map((tier) => (
            <option key={tier} value={tier}>
              {tier}
            </option>
          ))}
        </select>
      </label>
      <label>
        Status
        <select name="status" defaultValue="prospective" disabled={pending}>
          {statuses.map((status) => (
            <option key={status} value={status}>
              {status}
            </option>
          ))}
        </select>
      </label>
      <label>
        Contact email
        <input name="contactEmail" type="email" maxLength={320} disabled={pending} />
      </label>
      {canViewFinancials ? (
        <label>
          Committed (cents)
          <input name="amountCents" inputMode="numeric" disabled={pending} />
        </label>
      ) : null}
      {error ? <p className={styles.error}>{error}</p> : null}
      <button type="submit" className="button primary" disabled={pending}>
        Add sponsor
      </button>
      {!canViewFinancials ? (
        <p className={styles.note}>Amounts need sponsors.view_financials and are hidden from you.</p>
      ) : null}
    </form>
  );
}

export function EditSponsorForm({
  sponsor,
  canViewFinancials,
}: {
  sponsor: SponsorView;
  canViewFinancials: boolean;
}) {
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  return (
    <form
      className={styles.form}
      action={(form: FormData) =>
        start(async () => {
          setError(null);
          const result = await updateSponsor(sponsor.id, {}, form);
          setError(result.error ?? null);
        })
      }
    >
      <label>
        Name
        <input name="name" defaultValue={sponsor.name} maxLength={200} disabled={pending} />
      </label>
      <label>
        Tier
        <select name="tier" defaultValue={sponsor.tier} disabled={pending}>
          {tiers.map((tier) => (
            <option key={tier} value={tier}>
              {tier}
            </option>
          ))}
        </select>
      </label>
      <label>
        Status
        <select name="status" defaultValue={sponsor.status} disabled={pending}>
          {statuses.map((status) => (
            <option key={status} value={status}>
              {status}
            </option>
          ))}
        </select>
      </label>
      <label>
        Contact email
        <input
          name="contactEmail"
          type="email"
          defaultValue={sponsor.contactEmail ?? ""}
          maxLength={320}
          disabled={pending}
        />
      </label>
      {canViewFinancials ? (
        <label>
          Committed (cents)
          <input
            name="amountCents"
            inputMode="numeric"
            defaultValue={sponsor.amountCents ?? ""}
            disabled={pending}
          />
        </label>
      ) : null}
      {error ? <p className={styles.error}>{error}</p> : null}
      <button type="submit" className="button" disabled={pending}>
        Save
      </button>
    </form>
  );
}

export function DeleteSponsorButton({ id, name }: { id: string; name: string }) {
  const [error, setError] = useState<string | null>(null);
  const [pending, start] = useTransition();

  return (
    <>
      <button
        type="button"
        className="button danger"
        disabled={pending}
        onClick={() => {
          if (!window.confirm(`Remove ${name}?`)) return;
          start(async () => {
            const result = await deleteSponsor(id);
            setError(result.error ?? null);
          });
        }}
      >
        Remove
      </button>
      {error ? <p className={styles.error}>{error}</p> : null}
    </>
  );
}
