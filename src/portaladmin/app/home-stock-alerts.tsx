import Link from "next/link";
import { readView } from "./inventory/api";
import styles from "./home-stock-alerts.module.css";

/** Current stock, rather than email history: replenishment clears this warning. */
export async function HomeStockAlerts() {
  const result = await readView();
  if (!result.ok) {
    return (
      <section className={styles.alert} aria-label="Inventory alerts">
        <p>Inventory alerts could not be loaded. <Link href="/inventory">Check inventory</Link></p>
      </section>
    );
  }
  const { chosen, items } = result.view;
  const low = items.filter((item) => item.isLow);
  if (!chosen || low.length === 0) return null;
  const inventory = `/inventory?eventId=${encodeURIComponent(chosen.id)}`;

  return (
    <section className={styles.alert} aria-labelledby="stock-alert-title" role="status">
      <div className={styles.heading}>
        <div>
          <h2 id="stock-alert-title">Low stock · {low.length} {low.length === 1 ? "item" : "items"}</h2>
          <p>{chosen.name} — supplies need attention</p>
        </div>
        <Link href={inventory}>Review inventory →</Link>
      </div>
      <ul>
        {low.slice(0, 5).map((item) => (
          <li key={item.id}>
            <Link href={`${inventory}#item-${item.id}`}>{item.name}</Link>
            <span>{item.count} left · low at {item.lowAt}{item.location ? ` · ${item.location}` : ""}</span>
          </li>
        ))}
      </ul>
      {low.length > 5 ? <p>And {low.length - 5} more in inventory.</p> : null}
    </section>
  );
}
