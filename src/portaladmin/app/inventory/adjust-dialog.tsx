"use client";

import { ErrorToast } from "@/components/ui/error-toast";
import { useEffect, useRef, useState, useTransition } from "react";
import { Cancel01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { recordMovement } from "./actions";
import type { ItemView } from "./api";
import styles from "./inventory.module.css";

/**
 * Everything the +1 / -1 steppers do not cover: a specific amount, and why.
 *
 * Direction is its own choice rather than a signed field -- typing "-12"
 * into a plain box is a way to make the exact mistake that typing 12 into a
 * box already labelled "Take out" is not.
 *
 * A 400 here is the API naming what is actually on hand. It is shown
 * exactly as written, never replaced with a guess of this screen's own.
 */
export function AdjustDialog({ item }: { item: ItemView }) {
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);

  return (
    <>
      <button ref={trigger} type="button" className={styles.rowAction} onClick={() => setOpen(true)}>
        Adjust
      </button>
      {open ? (
        <AdjustForm
          item={item}
          onClose={() => {
            setOpen(false);
            trigger.current?.focus();
          }}
        />
      ) : null}
    </>
  );
}

function AdjustForm({ item, onClose }: { item: ItemView; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const amountInput = useRef<HTMLInputElement>(null);
  const [direction, setDirection] = useState<"in" | "out">("out");
  const [amount, setAmount] = useState("1");
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, startSaving] = useTransition();

  useEffect(() => {
    dialog.current?.showModal();
    amountInput.current?.focus();
  }, []);

  function save() {
    const magnitude = Number(amount);
    if (!Number.isInteger(magnitude) || magnitude <= 0) {
      setError("Enter a whole number greater than zero.");
      return;
    }

    setError(null);
    startSaving(async () => {
      const result = await recordMovement(
        item.id,
        direction === "out" ? -magnitude : magnitude,
        reason.trim() || null,
      );

      if (result.error) {
        setError(result.error);
      } else {
        dialog.current?.close();
      }
    });
  }

  return (
    <dialog
      ref={dialog}
      className={styles.dialog}
      aria-labelledby="adjust-title"
      aria-describedby="adjust-description"
      onClose={onClose}
      onCancel={(event) => {
        if (pending) event.preventDefault();
      }}
    >
      <form
        onSubmit={(event) => {
          event.preventDefault();
          save();
        }}
      >
        <div className={styles.dialogHeader}>
          <h2 id="adjust-title">Adjust {item.name}</h2>
          <button
            type="button"
            className={styles.closeButton}
            aria-label="Close"
            disabled={pending}
            onClick={() => dialog.current?.close()}
          >
            <Icon icon={Cancel01Icon} size={20} />
          </button>
        </div>
        <p id="adjust-description" className={styles.dialogDescription}>
          Currently <strong>{item.count.toLocaleString("en-US")}</strong> on hand.
        </p>

        <fieldset className={styles.directionChoice} disabled={pending}>
          <legend className={styles.visuallyHidden}>Direction</legend>
          <label data-checked={direction === "in" || undefined}>
            <input
              type="radio"
              name="direction"
              value="in"
              checked={direction === "in"}
              onChange={() => setDirection("in")}
            />
            Add
          </label>
          <label data-checked={direction === "out" || undefined}>
            <input
              type="radio"
              name="direction"
              value="out"
              checked={direction === "out"}
              onChange={() => setDirection("out")}
            />
            Take out
          </label>
        </fieldset>

        <div className={styles.field}>
          <label htmlFor="adjust-amount">Amount</label>
          <input
            ref={amountInput}
            id="adjust-amount"
            type="number"
            inputMode="numeric"
            min={1}
            step={1}
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            disabled={pending}
          />
        </div>

        <div className={styles.field}>
          <label htmlFor="adjust-reason">
            Reason <span className={styles.optional}>(optional)</span>
          </label>
          <input
            id="adjust-reason"
            autoComplete="off"
            placeholder="e.g. handed out at check-in"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            disabled={pending}
          />
        </div>

        <ErrorToast message={error} />
        <div className={styles.dialogActions}>
          <button type="button" disabled={pending} onClick={() => dialog.current?.close()}>
            Cancel
          </button>
          <button type="submit" className="button primary" disabled={pending}>
            {pending ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </dialog>
  );
}
