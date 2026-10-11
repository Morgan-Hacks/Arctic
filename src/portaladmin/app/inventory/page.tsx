import { EmptyState } from "@/components/ui/empty-state";
import { readPageData } from "@/lib/page-data";
import { Shell } from "../shell";
import { AddItemDialog } from "./add-item-dialog";
import { EventPicker } from "./event-picker";
import { ItemCard } from "./item-card";
import styles from "./inventory.module.css";
import { readView } from "./api";

/**
 * What logistics has on hand, for one event.
 *
 * Three questions, same as every other screen here: what is there, has
 * anything run low, and what happened to a given item. +1 / -1 on each row
 * covers the common case -- taking one shirt out should be one tap, not a
 * form -- with a dialog underneath for a specific amount and a reason.
 *
 * Every route under /admin/inventory asks for the same permission, so there
 * is no courtesy split here between a reader who may look and one who may
 * not: reaching this screen at all already means the count buttons work.
 */
export default async function Inventory({
  searchParams,
}: {
  searchParams: Promise<{ eventId?: string }>;
}) {
  const { eventId } = await searchParams;
  const { person, data: read } = await readPageData(() => readView(eventId));

  if (!read.ok) {
    return (
      <Shell personId={person.personId}>
        <h1>Inventory</h1>
        <div className="empty">{read.error}</div>
      </Shell>
    );
  }

  const { events, chosen, items } = read.view;

  if (!chosen) {
    return (
      <Shell personId={person.personId}>
        <h1>Inventory</h1>
        <EmptyState
          variant="data"
          size="page"
          title="No events yet"
          description="Create an event under Events. Inventory is tracked per event."
        />
      </Shell>
    );
  }

  return (
    <Shell personId={person.personId}>
      <div className={styles.page}>
        <div className="page-head">
          <div>
            <h1>Inventory</h1>
            <p className="lede">What logistics has on hand for this event.</p>
          </div>
          <div className="page-actions">
            <AddItemDialog eventId={chosen.id} />
          </div>
        </div>

        <EventPicker events={events} chosen={chosen} />

        {items.length === 0 ? (
          <EmptyState
            variant="data"
            size="page"
            title="Nothing logged yet"
            description="Add the first item to start tracking what logistics has on hand."
          />
        ) : (
          <ul className={styles.list} aria-label="Inventory items">
            {items.map((item) => (
              <ItemCard key={item.id} item={item} />
            ))}
          </ul>
        )}
      </div>
    </Shell>
  );
}
