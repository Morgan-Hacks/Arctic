"use client";

import { ErrorToast } from "@/components/ui/error-toast";
import { useActionState } from "react";
import { MinusSignIcon, PackageIcon, PlusSignIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { AdjustDialog } from "./adjust-dialog";
import { DeleteItemDialog } from "./delete-item-dialog";
import { EditItemDialog } from "./edit-item-dialog";
import { HistoryDialog } from "./history-dialog";
import { bump, type MovementState } from "./actions";
import type { ItemView } from "./api";
import styles from "./inventory.module.css";

/**
 * One line of stock.
 *
 * The count is the first thing anybody opens this screen to read, so it is
 * the largest thing on the card. A low item is not only labelled -- the pill
 * is easy to miss at the end of a row somebody is skimming -- the count
 * itself takes the warning colour, on the number a reader is already
 * looking at.
 */
export function ItemCard({ item }: { item: ItemView }) {
  return (
    <li className={styles.item} data-low={item.isLow || undefined}>
      <div className={styles.itemMain}>
        {item.imageUrl ? (
          <img src={item.imageUrl} alt="" className={styles.thumb} />
        ) : (
          <span className={styles.thumbPlaceholder} aria-hidden="true">
            <Icon icon={PackageIcon} size={22} />
          </span>
        )}

        <div className={styles.itemText}>
          <strong className={styles.itemName}>{item.name}</strong>
          {item.category || item.location ? (
            <span className={styles.itemMeta}>
              {[item.category, item.location].filter(Boolean).join(" · ")}
            </span>
          ) : null}
          {item.note ? <span className={styles.itemNote}>{item.note}</span> : null}
        </div>
      </div>

      <div className={styles.quantity}>
        <div className={styles.itemCount}>
          <span className={styles.count}>{item.count.toLocaleString("en-US")}</span>
          {item.isLow ? <span className={styles.lowPill}>Low</span> : null}
        </div>

        <div className={styles.step}>
          <Bump id={item.id} name={item.name} delta={-1} />
          <Bump id={item.id} name={item.name} delta={1} />
        </div>
      </div>

      <div className={styles.itemActions}>
        <AdjustDialog item={item} />
        <HistoryDialog item={item} />
        <EditItemDialog item={item} />
        <DeleteItemDialog item={item} />
      </div>
    </li>
  );
}

/**
 * +1 or -1, each its own one-field form.
 *
 * Its own component, and its own pending/error state, for the same reason
 * Remove in people/[id]/controls.tsx is: a row's failure -- taking out more
 * than is on hand -- has to report against the button that was pressed, not
 * against whichever item happened to load first on the page.
 */
function Bump({ id, name, delta }: { id: string; name: string; delta: 1 | -1 }) {
  const [state, submit, pending] = useActionState<MovementState, FormData>(bump, {});

  return (
    <form action={submit}>
      <input type="hidden" name="id" value={id} />
      <input type="hidden" name="delta" value={delta} />
      <button
        type="submit"
        className={styles.stepButton}
        disabled={pending}
        aria-label={delta > 0 ? `Add one to ${name}` : `Take one out of ${name}`}
      >
        <Icon icon={delta > 0 ? PlusSignIcon : MinusSignIcon} size={18} />
      </button>
      <ErrorToast message={state.error} revision={state} />
    </form>
  );
}
