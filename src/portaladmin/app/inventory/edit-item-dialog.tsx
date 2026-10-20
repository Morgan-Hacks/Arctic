"use client";

import { ErrorToast } from "@/components/ui/error-toast";
import { useEffect, useRef, useState, useTransition } from "react";
import { Cancel01Icon, Edit02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { updateItem } from "./actions";
import { ImageField } from "./image-field";
import type { ItemView } from "./api";
import styles from "./inventory.module.css";

/**
 * An item's details. Never its count -- that field does not exist on this
 * form, because changing it here would be a count with no row in the
 * history to explain where it went.
 */
export function EditItemDialog({ item }: { item: ItemView }) {
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);

  return (
    <>
      <button ref={trigger} type="button" className={styles.rowAction} onClick={() => setOpen(true)}>
        <Icon icon={Edit02Icon} size={14} />
        Edit
      </button>
      {open ? (
        <EditItemForm
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

function EditItemForm({ item, onClose }: { item: ItemView; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const nameInput = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(item.name);
  const [category, setCategory] = useState(item.category ?? "");
  const [location, setLocation] = useState(item.location ?? "");
  const [note, setNote] = useState(item.note ?? "");
  const [lowAt, setLowAt] = useState(item.lowAt === null ? "" : String(item.lowAt));
  const [error, setError] = useState<string | null>(null);
  const [pending, startSaving] = useTransition();

  useEffect(() => {
    dialog.current?.showModal();
    nameInput.current?.focus();
  }, []);

  function save() {
    setError(null);
    startSaving(async () => {
      const result = await updateItem(item.id, {
        name,
        category: category.trim() || null,
        location: location.trim() || null,
        note: note.trim() || null,
        lowAt: lowAt.trim() === "" ? null : Number(lowAt),
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
      aria-labelledby="edit-item-title"
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
          <h2 id="edit-item-title">Edit {item.name}</h2>
          <button
            type="button"
            className={styles.closeButton}
            aria-label="Close edit item"
            disabled={pending}
            onClick={() => dialog.current?.close()}
          >
            <Icon icon={Cancel01Icon} size={20} />
          </button>
        </div>

        {/* Its own write, not part of Save -- see ImageField. */}
        <ImageField item={item} />

        <div className={styles.field}>
          <label htmlFor="edit-item-name">Name</label>
          <input
            ref={nameInput}
            id="edit-item-name"
            required
            autoComplete="off"
            value={name}
            onChange={(event) => setName(event.target.value)}
            disabled={pending}
          />
        </div>

        <div className={styles.fieldRow}>
          <div className={styles.field}>
            <label htmlFor="edit-item-category">
              Category <span className={styles.optional}>(optional)</span>
            </label>
            <input
              id="edit-item-category"
              autoComplete="off"
              value={category}
              onChange={(event) => setCategory(event.target.value)}
              disabled={pending}
            />
          </div>
          <div className={styles.field}>
            <label htmlFor="edit-item-location">
              Location <span className={styles.optional}>(optional)</span>
            </label>
            <input
              id="edit-item-location"
              autoComplete="off"
              value={location}
              onChange={(event) => setLocation(event.target.value)}
              disabled={pending}
            />
          </div>
        </div>

        <div className={styles.field}>
          <label htmlFor="edit-item-note">
            Note <span className={styles.optional}>(optional)</span>
          </label>
          <textarea
            id="edit-item-note"
            rows={2}
            value={note}
            onChange={(event) => setNote(event.target.value)}
            disabled={pending}
          />
        </div>

        <div className={styles.field}>
          <label htmlFor="edit-item-low-at">
            Low at <span className={styles.optional}>(optional)</span>
          </label>
          <input
            id="edit-item-low-at"
            type="number"
            inputMode="numeric"
            min={0}
            step={1}
            value={lowAt}
            onChange={(event) => setLowAt(event.target.value)}
            disabled={pending}
            aria-describedby="edit-item-low-at-help"
          />
        </div>
        {/* COPY: needs sign-off */}
        <p id="edit-item-low-at-help" className={styles.hint}>
          Shown on the dashboard at or below this count. Crossing from above emails organizers with inventory access. Leave blank to disable alerts.
        </p>

        <ErrorToast message={error} />
        <div className={styles.dialogActions}>
          <button type="button" disabled={pending} onClick={() => dialog.current?.close()}>
            Cancel
          </button>
          <button type="submit" className="button primary" disabled={pending || !name.trim()}>
            {pending ? "Saving…" : "Save"}
          </button>
        </div>
      </form>
    </dialog>
  );
}
