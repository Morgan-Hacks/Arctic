"use client";

import { useEffect, useRef, useState, useTransition } from "react";
import { Cancel01Icon, Clock01Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { loadMovements } from "./actions";
import type { ItemView, MovementView } from "./api";
import styles from "./inventory.module.css";

/**
 * Who moved an item's count, by how much, and when.
 *
 * Loaded on open rather than kept with the list: a history nobody opens is a
 * round trip nobody needed, and this one is read far less often than the
 * count itself is.
 */
export function HistoryDialog({ item }: { item: ItemView }) {
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);

  return (
    <>
      <button ref={trigger} type="button" className={styles.rowAction} onClick={() => setOpen(true)}>
        <Icon icon={Clock01Icon} size={14} />
        History
      </button>
      {open ? (
        <HistoryPanel
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

type Load =
  | { status: "loading" }
  | { status: "ok"; movements: MovementView[] }
  | { status: "failed"; error: string };

function HistoryPanel({ item, onClose }: { item: ItemView; onClose: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [load, setLoad] = useState<Load>({ status: "loading" });
  const [, startLoading] = useTransition();

  useEffect(() => {
    dialog.current?.showModal();
    startLoading(async () => {
      const result = await loadMovements(item.id);
      setLoad(
        result.ok ? { status: "ok", movements: result.movements } : { status: "failed", error: result.error },
      );
    });
    // Only on open. item.id does not change under one dialog instance.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <dialog
      ref={dialog}
      className={styles.dialog}
      aria-labelledby="history-title"
      onClose={onClose}
      onCancel={() => dialog.current?.close()}
    >
      <div className={styles.dialogHeader}>
        <h2 id="history-title">History · {item.name}</h2>
        <button
          type="button"
          className={styles.closeButton}
          aria-label="Close history"
          onClick={() => dialog.current?.close()}
        >
          <Icon icon={Cancel01Icon} size={20} />
        </button>
      </div>

      {load.status === "loading" ? (
        <p className={styles.dialogDescription} role="status">
          Loading…
        </p>
      ) : load.status === "failed" ? (
        <p className={styles.dialogDescription}>{load.error}</p>
      ) : load.movements.length === 0 ? (
        <p className={styles.dialogDescription}>Nothing recorded yet.</p>
      ) : (
        <ul className={styles.movements}>
          {load.movements.map((movement) => (
            <li key={movement.id} className={styles.movement}>
              <span className={styles.delta} data-sign={movement.delta >= 0 ? "in" : "out"}>
                {movement.delta >= 0 ? "+" : ""}
                {movement.delta.toLocaleString("en-US")}
              </span>
              <span className={styles.balance}>&rarr; {movement.balance.toLocaleString("en-US")}</span>
              <span className={styles.movementMeta}>
                {/* Null is the honest answer when the record does not say who --
                    inventing a name here would be believed. */}
                {movement.actor ?? "No organizer recorded"} · {stamp(movement.occurredAt)}
              </span>
              {movement.reason ? <span className={styles.movementReason}>{movement.reason}</span> : null}
            </li>
          ))}
        </ul>
      )}

      <div className={styles.dialogActions}>
        <button type="button" onClick={() => dialog.current?.close()}>
          Close
        </button>
      </div>
    </dialog>
  );
}

/**
 * A timestamp as something to compare, not to read as prose.
 *
 * UTC, sliced rather than formatted with the browser's locale -- the server
 * and the client would otherwise render two different strings for the same
 * instant, which React reports as a hydration mismatch and a reader
 * experiences as the page flickering.
 */
function stamp(iso: string): string {
  return iso.length >= 16 ? `${iso.slice(0, 10)} ${iso.slice(11, 16)}` : iso;
}
