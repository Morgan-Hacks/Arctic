"use client";

import { useEffect, useRef, useState, useTransition, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import { ArrowUpRight01Icon, Cancel01Icon, ChartHistogramIcon, RefreshIcon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { EmptyState } from "@/components/ui/empty-state";
import { when, type CampaignAnalytics, type MessageProgress } from "./types";
import styles from "./campaign-report.module.css";

const number = new Intl.NumberFormat("en-US");
const percent = new Intl.NumberFormat("en-US", { style: "percent", maximumFractionDigits: 1 });
const rate = (value: number, total: number) => total > 0 ? percent.format(value / total) : "—";
type Content = NonNullable<CampaignAnalytics["content"]>;
type Breakdown = { name: string; count: number }[];
type Point = { bucket: string; opens: number; clicks: number };

// Hidden for the thumbnail, kept for the dialog, for the reason spelled out
// over emailDocument in components/templates/email-preview.
function document(html: string, { scrollbars = false }: { scrollbars?: boolean } = {}) {
  const bars = scrollbars ? "" : "*{scrollbar-width:none}*::-webkit-scrollbar{display:none;width:0;height:0}";
  return `<!doctype html><html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><style>:root{color-scheme:light}html,body{margin:0;background:#fff;color:#14161a}body{font-family:Arial,sans-serif}img{max-width:100%}${bars}</style></head><body>${html}</body></html>`;
}

export function ReportRefresh() {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  return <button type="button" className={styles.refresh} disabled={pending} onClick={() => startTransition(() => router.refresh())}>
    <Icon icon={RefreshIcon} size={16} />{pending ? "Refreshing…" : "Refresh report"}
  </button>;
}

export function CampaignContent({ content, children }: { content: Content; children?: ReactNode }) {
  const [expanded, setExpanded] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { if (expanded) dialog.current?.showModal(); }, [expanded]);
  return <>
    <div className={styles.emailOverview}>
      <button type="button" className={styles.previewButton} onClick={() => setExpanded(true)} aria-label="View full email preview">
        <span className={styles.thumbnail} aria-hidden="true"><iframe title="Email thumbnail" tabIndex={-1} sandbox="" referrerPolicy="no-referrer" srcDoc={document(content.html)} /></span>
        <span>View email<Icon icon={ArrowUpRight01Icon} size={15} /></span>
      </button>
      <div className={styles.emailDetails}><dl className={styles.emailSettings}>
        <div className={styles.subject}><dt>Email subject</dt><dd>{content.subject}</dd></div>
        {content.previewText ? <div className={styles.subject}><dt>Preview text</dt><dd>{content.previewText}</dd></div> : null}
        <div><dt>From name</dt><dd>{content.fromName || "Not set"}</dd></div>
        <div><dt>Sender address</dt><dd>{content.fromEmail}</dd></div>
        {content.replyTo ? <div><dt>Reply to</dt><dd>{content.replyTo}</dd></div> : null}
        <div><dt>Preview source</dt><dd>{content.isSample ? "A message from this campaign" : "Campaign template"}</dd></div>
      </dl>{children}</div>
    </div>
    {expanded ? <dialog ref={dialog} className={styles.previewDialog} onClose={() => setExpanded(false)} aria-labelledby="campaign-email-preview-title">
      <header><div><h2 id="campaign-email-preview-title">Email preview</h2><p>{content.subject}</p></div>
        <button type="button" onClick={() => dialog.current?.close()} aria-label="Close email preview"><Icon icon={Cancel01Icon} size={20} /></button></header>
      <iframe title="Campaign email preview" sandbox="" referrerPolicy="no-referrer" srcDoc={document(content.html, { scrollbars: true })} />
    </dialog> : null}
  </>;
}

export function CampaignMetrics({ analytics, messages }: { analytics: CampaignAnalytics; messages: MessageProgress | null }) {
  const e = analytics.engagement;
  const metrics = [
    { label: "Emails sent", tone: "sky", value: number.format(analytics.sentEmails), hint: "Accepted for sending" },
    { label: "Delivered", tone: "teal", value: messages ? number.format(messages.byStatus.delivered ?? 0) : "—", hint: "Confirmed by the provider" },
    { label: "Open rate", tone: "violet", value: rate(e.openedEmails, e.openTrackedEmails), hint: e.openTrackedEmails ? `${number.format(e.openedEmails)} unique opens · ${number.format(e.openTrackedEmails)} tracked` : "Open tracking unavailable" },
    { label: "Click rate", tone: "amber", value: rate(analytics.clickedEmails, analytics.trackedEmails), hint: analytics.trackedEmails ? `${number.format(analytics.clickedEmails)} emails clicked · ${number.format(analytics.trackedEmails)} tracked` : "No sent emails with tracked links" },
    { label: "Bounced", tone: "rose", value: messages ? number.format(messages.byStatus.bounced ?? 0) : "—", hint: "Reported delivery failures" },
    { label: "Spam complaints", tone: "rose", value: messages ? number.format(messages.byStatus.complained ?? 0) : "—", hint: "Reported by the provider" },
    { label: "Total opens", tone: "violet", value: e.openTrackedEmails ? number.format(e.totalOpens) : "—", hint: "Includes repeat opens" },
    { label: "Link clicks", tone: "amber", value: number.format(analytics.totalClicks), hint: "Includes repeat clicks" },
  ];
  return <div className={styles.performance}>
    <dl className={styles.metrics}>{metrics.map(metric => <div key={metric.label} data-tone={metric.tone}>
      <dt>{metric.label}</dt><dd>{metric.value}</dd><p>{metric.hint}</p>
    </div>)}</dl>
    <p className={styles.note}>Email privacy features and automated security checks can affect opens and clicks.</p>
  </div>;
}

export function CampaignReports({ analytics }: { analytics: CampaignAnalytics }) {
  const e = analytics.engagement;
  const regions = new Intl.DisplayNames(["en"], { type: "region" });
  const countries = e.countries.map(item => ({ ...item, name: item.name === "Unknown" ? "Unknown location" : regions.of(item.name) || item.name }));
  return <div className={styles.reports}>
    <section className={styles.linksReport} aria-labelledby="campaign-links-title">
      <header><div><h2 id="campaign-links-title">Link performance</h2><p>Top 10 links, ranked by total clicks</p></div></header>
      {analytics.links === null ? <p className={styles.restricted}>Link destinations are available to organizers with template access.</p>
        : analytics.links.length ? <div className={styles.linkTableWrap}><table className={styles.linkTable}>
          <thead><tr><th scope="col">Destination</th><th scope="col">Total clicks</th><th scope="col">Unique clicks</th><th scope="col">Last clicked</th></tr></thead>
          <tbody>{analytics.links.map((link, index) => <tr key={link.destination}>
            <td><div className={styles.linkIdentity}><span className={styles.linkRank}>{String(index + 1).padStart(2, "0")}</span><span className={styles.destination} title={link.destination}><strong>{linkParts(link.destination).host}</strong><small>{linkParts(link.destination).path}</small></span></div></td>
            <td data-label="Total clicks"><strong className={styles.clickTotal}>{number.format(link.totalClicks)}</strong></td>
            <td data-label="Unique clicks"><strong className={styles.uniqueTotal}>{number.format(link.clickedEmails)}</strong><small>{rate(link.clickedEmails, link.trackedEmails)} of tracked emails</small></td>
            <td data-label="Last clicked">{link.lastClickedAt ? <time dateTime={link.lastClickedAt}>{when(link.lastClickedAt)}</time> : "—"}</td>
          </tr>)}</tbody>
        </table></div> : <EmptyState variant="data" size="compact" title="No tracked links yet" description="Links will appear after a campaign with email tracking enabled is sent." />}
    </section>
    <div className={styles.chartGrid}>
      <Activity title="Activity by day" points={fillDays(e.daily)} hourly={false} />
      <Activity title="Activity by hour" points={Array.from({ length: 24 }, (_, hour) => e.hourly.find(point => Number(point.bucket) === hour) ?? { bucket: String(hour).padStart(2, "0"), opens: 0, clicks: 0 })} hourly />
    </div>
    {analytics.totalClicks > e.recordedClicks ? <p className={styles.note}>Historical click totals are included above. Activity and device reports cover individually recorded events.</p> : null}
    <div className={styles.breakdownGrid}>
      <ClientBreakdown title="Browser" items={e.browsers} />
      <ClientBreakdown title="Operating system" items={e.operatingSystems} />
      <ClientBreakdown title="Platform" items={e.platforms} />
      <ClientBreakdown title="Location" items={countries} location />
    </div>
  </div>;
}

function linkParts(destination: string) {
  try {
    const url = new URL(destination);
    return { host: url.host, path: url.pathname + url.search + url.hash };
  } catch {
    return { host: destination, path: "" };
  }
}

function fillDays(points: Point[]): Point[] {
  if (points.length < 2) return points;
  const result: Point[] = [];
  const last = Date.parse(points[points.length - 1].bucket + "T00:00:00Z");
  for (let at = Date.parse(points[0].bucket + "T00:00:00Z"); at <= last; at += 86400000) {
    const bucket = new Date(at).toISOString().slice(0, 10);
    result.push(points.find(point => point.bucket === bucket) ?? { bucket, opens: 0, clicks: 0 });
  }
  return result;
}

function Activity({ title, points, hourly }: { title: string; points: Point[]; hourly: boolean }) {
  const [series, setSeries] = useState<"both" | "opens" | "clicks">("both");
  const active = points.some(point => point.opens > 0 || point.clicks > 0);
  const max = Math.max(2, Math.ceil(Math.max(0, ...points.map(point => Math.max(series === "clicks" ? 0 : point.opens, series === "opens" ? 0 : point.clicks))) / 2) * 2);
  const x = (index: number) => points.length === 1 ? 250 : 42 + index / Math.max(1, points.length - 1) * 430;
  const y = (value: number) => 174 - value / max * 134;
  const label = (bucket: string) => hourly ? `${bucket}:00` : new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", timeZone: "UTC" }).format(new Date(bucket + "T00:00:00Z"));
  return <section className={styles.report}>
    <header><div><h2>{title}</h2><p>Recorded events · Last 90 days · UTC</p></div><Icon icon={ChartHistogramIcon} size={18} /></header>
    {active ? <>
      <div className={styles.chartControls} role="group" aria-label={`${title} series`}>
        {(["both", "opens", "clicks"] as const).map(value => <button key={value} type="button" aria-pressed={series === value} onClick={() => setSeries(value)}>{value === "both" ? "All activity" : value === "opens" ? "Opens" : "Clicks"}</button>)}
      </div>
      <svg className={styles.chart} viewBox="0 0 500 210" role="img" aria-label={`${title}: ${number.format(points.reduce((sum, point) => sum + point.opens, 0))} opens and ${number.format(points.reduce((sum, point) => sum + point.clicks, 0))} clicks`}>
        {[0, .5, 1].map(tick => <g key={tick}><line x1="42" x2="472" y1={y(max * tick)} y2={y(max * tick)} className={styles.gridLine} /><text x="32" y={y(max * tick) + 4} textAnchor="end">{number.format(Math.round(max * tick))}</text></g>)}
        {(["opens", "clicks"] as const).filter(key => series === "both" || series === key).map(key => <g key={key} className={key === "opens" ? styles.openSeries : styles.clickSeries}>
          <polyline points={points.map((point, index) => `${x(index)},${y(point[key])}`).join(" ")} fill="none" strokeWidth="2" />
          {points.map((point, index) => <circle key={point.bucket} cx={x(index)} cy={y(point[key])} r={points.length > 40 ? 2 : 3} tabIndex={0} aria-label={`${label(point.bucket)}: ${point[key]} ${key}`}><title>{`${label(point.bucket)}: ${point[key]} ${key}`}</title></circle>)}
        </g>)}
        {points.filter((_, index) => index === 0 || index === points.length - 1 || (points.length > 4 && index === Math.floor(points.length / 2))).map(point => <text key={point.bucket} x={x(points.indexOf(point))} y="198" textAnchor="middle">{label(point.bucket)}</text>)}
      </svg>
      <div className={styles.legend}>{series !== "clicks" ? <span className={styles.opensDot}>Opens</span> : null}{series !== "opens" ? <span className={styles.clicksDot}>Clicks</span> : null}</div>
    </> : <EmptyState variant="data" size="compact" title="No activity recorded yet" description="Opens and clicks from tracked emails will appear here." />}
  </section>;
}

function ClientBreakdown({ title, items, location = false }: { title: string; items: Breakdown; location?: boolean }) {
  const total = items.reduce((sum, item) => sum + item.count, 0);
  const known = items.some(item => item.name !== "Unknown location");
  return <section className={styles.report}>
    <header><div><h2>{title}</h2><p>Based on recorded link clicks</p></div></header>
    {items.length && (!location || known) ? <ul className={styles.breakdown}>{items.map(item => <li key={item.name}>
      <div><span>{item.name}</span><strong>{rate(item.count, total)}<small>{number.format(item.count)}</small></strong></div>
      <div className={styles.bar}><span style={{ width: `${item.count / total * 100}%` }} /></div>
    </li>)}</ul> : <EmptyState variant="data" size="compact" title={location ? "No location data" : "No device activity yet"}
      description={location ? "Countries appear as recipients follow tracked links." : "This report fills in as recipients follow tracked links."} />}
    {location ? <p className={styles.note}>Approximate country from link clicks · <a href="https://db-ip.com" target="_blank" rel="noreferrer">IP Geolocation by DB-IP</a></p> : null}
  </section>;
}
