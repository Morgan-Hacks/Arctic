"use client";

import { ErrorToast } from "@/components/ui/error-toast";
import { useEffect, useRef, useState } from "react";
import { Cancel01Icon, Delete02Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { deleteItem } from "./actions";
import type { ItemView } from "./api";
import styles from "./inventory.module.css";

/** Removes an item from the list. Its count and history go with it. */
export function DeleteItemDialog({ item }: { item: ItemView }) {
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);

  return (
    <>
      <button
        ref={trigger}
        type="button"
        className={`${styles.rowAction} ${styles.danger}`}
        onClick={() => setOpen(true)}
      >
        <Icon icon={Delete02Icon} size={14} />
        Delete
      </button>
      {open ? (
        <DeleteItemConfirm
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

function DeleteItemConfirm({ item, onClose }: { item: ItemView; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const cancel = useRef<HTMLButtonElement>(null);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    dialog.current?.showModal();
    cancel.current?.focus();
  }, []);

  async function confirm() {
    setPending(true);
    setError(null);
    const result = await deleteItem(item.id);
    setPending(false);

    if (result.error) {
      setError(result.error);
    } else {
      dialog.current?.close();
    }
  }

  return (
    <dialog
      ref={dialog}
      className={styles.dialog}
      aria-labelledby="delete-item-title"
      aria-describedby="delete-item-description"
      onClose={onClose}
      onCancel={(event) => {
        if (pending) event.preventDefault();
      }}
    >
      <div className={styles.dialogHeader}>
        <h2 id="delete-item-title">Delete {item.name}?</h2>
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
      <p id="delete-item-description" className={styles.dialogDescription}>
        This cannot be undone.
      </p>
      <ErrorToast message={error} />
      <div className={styles.dialogActions}>
        <button ref={cancel} type="button" disabled={pending} onClick={() => dialog.current?.close()}>
          Cancel
        </button>
        <button type="button" className="button danger" disabled={pending} onClick={() => void confirm()}>
          {pending ? "Deleting…" : "Delete item"}
        </button>
      </div>
    </dialog>
  );
}
