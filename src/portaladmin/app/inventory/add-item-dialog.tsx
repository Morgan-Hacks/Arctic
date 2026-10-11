"use client";

import { ErrorToast } from "@/components/ui/error-toast";
import { useEffect, useRef, useState, useTransition } from "react";
import { Add01Icon, Cancel01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { createItem } from "./actions";
import styles from "./inventory.module.css";

/**
 * Starts a new item at whatever is already on hand.
 *
 * The starting count is a field here rather than always zero followed by
 * pressing +1 by hand -- counting forty shirts in and then tapping a stepper
 * forty times is not a workflow, it is a chore the form can skip.
 */
export function AddItemDialog({ eventId }: { eventId: string }) {
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);

  return (
    <>
      <button ref={trigger} type="button" className="button primary" onClick={() => setOpen(true)}>
        <Icon icon={Add01Icon} size={18} />
        Add item
      </button>
      {open ? (
        <AddItemForm
          eventId={eventId}
          onClose={() => {
            setOpen(false);
            trigger.current?.focus();
          }}
        />
      ) : null}
    </>
  );
}

function AddItemForm({ eventId, onClose }: { eventId: string; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const nameInput = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [category, setCategory] = useState("");
  const [location, setLocation] = useState("");
  const [note, setNote] = useState("");
  const [count, setCount] = useState("0");
  const [lowAt, setLowAt] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, startSaving] = useTransition();

  useEffect(() => {
    dialog.current?.showModal();
    nameInput.current?.focus();
  }, []);

  function save() {
    const startingCount = Number(count);
    if (!Number.isInteger(startingCount) || startingCount < 0) {
      setError("Starting count has to be zero or a whole number above it.");
      return;
    }

    setError(null);
    startSaving(async () => {
      const result = await createItem(eventId, {
        name,
        category: category.trim() || null,
        location: location.trim() || null,
        note: note.trim() || null,
        lowAt: lowAt.trim() === "" ? null : Number(lowAt),
        count: startingCount,
      });

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
      aria-labelledby="add-item-title"
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
          <h2 id="add-item-title">Add item</h2>
          <button
            type="button"
            className={styles.closeButton}
            aria-label="Close add item"
            disabled={pending}
            onClick={() => dialog.current?.close()}
          >
            <Icon icon={Cancel01Icon} size={20} />
          </button>
        </div>

        <div className={styles.field}>
          <label htmlFor="item-name">Name</label>
          <input
            ref={nameInput}
            id="item-name"
            required
            autoComplete="off"
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={pending}
          />
        </div>

        <div className={styles.fieldRow}>
          <div className={styles.field}>
            <label htmlFor="item-category">
              Category <span className={styles.optional}>(optional)</span>
            </label>
            <input
              id="item-category"
              autoComplete="off"
              value={category}
              onChange={(event) => setCategory(event.target.value)}
              disabled={pending}
            />
          </div>
          <div className={styles.field}>
            <label htmlFor="item-location">
              Location <span className={styles.optional}>(optional)</span>
            </label>
            <input
              id="item-location"
              autoComplete="off"
              value={location}
              onChange={(event) => setLocation(event.target.value)}
              disabled={pending}
            />
          </div>
        </div>

        <div className={styles.field}>
          <label htmlFor="item-note">
            Note <span className={styles.optional}>(optional)</span>
          </label>
          <textarea
            id="item-note"
            rows={2}
            value={note}
            onChange={(event) => setNote(event.target.value)}
            disabled={pending}
          />
        </div>

        <div className={styles.fieldRow}>
          <div className={styles.field}>
            <label htmlFor="item-count">Starting count</label>
            <input
              id="item-count"
              type="number"
              inputMode="numeric"
              min={0}
              step={1}
              value={count}
              onChange={(event) => setCount(event.target.value)}
              disabled={pending}
            />
          </div>
          <div className={styles.field}>
            <label htmlFor="item-low-at">
              Low at <span className={styles.optional}>(optional)</span>
            </label>
            <input
              id="item-low-at"
              type="number"
              inputMode="numeric"
              min={0}
              step={1}
              value={lowAt}
              onChange={(event) => setLowAt(event.target.value)}
              disabled={pending}
              aria-describedby="item-low-at-help"
            />
          </div>
        </div>
        {/* COPY: needs sign-off */}
        <p id="item-low-at-help" className={styles.hint}>
          Flagged as low once the count falls to this or below.
        </p>

        <ErrorToast message={error} />
        <div className={styles.dialogActions}>
          <button type="button" disabled={pending} onClick={() => dialog.current?.close()}>
            Cancel
          </button>
          <button type="submit" className="button primary" disabled={pending || !name.trim()}>
            {pending ? "Adding…" : "Add item"}
          </button>
        </div>
      </form>
    </dialog>
  );
}
