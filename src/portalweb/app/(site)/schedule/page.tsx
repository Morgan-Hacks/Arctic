import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { siteConfig } from "@/site.config";
import { EVENT_SITE, isOn } from "@/lib/features";
import { Frame, SectionHead } from "../../../components/event-site/frame";
import { ArrowRight } from "../../../components/event-site/icons";

const { schedule, contactEmail } = siteConfig;

export const metadata: Metadata = {
  title: `The weekend — MorganHacks ${siteConfig.year}`,
  description:
    "The provisional run of show for MorganHacks 2027: doors, the opening session, workshops, meals, the submission deadline and judging.",
};

/**
 * The run of show, both days.
 *
 * The banner at the top is not decoration and not an apology. Every time on
 * this page comes from site.config.ts with `provisional: true` next to it, and
 * a schedule that looks settled is one people book flights and shifts around.
 * The banner renders from that flag, so it cannot be forgotten about
 * separately from the times being made real -- turning it off and making them
 * real are the same edit.
 */
export default function SchedulePage() {
  // 404 rather than a styled "nothing here". A page that explains it is not
  // available yet is itself the tell that something is coming, and the bar in
  // docs/feature-flags.md is that a feature which is off should be
  // indistinguishable from one that was never built.
  if (!isOn(EVENT_SITE)) notFound();

  return (
    <Frame current="schedule">
      <section>
        <span className="es-rule" aria-hidden="true" />
        <div className="es-kicker">
          <p className="es-hero__eyebrow">Run of show</p>
          {schedule.provisional ? <span className="es-pill">Not final</span> : null}
        </div>

        <h1 className="es-hero__title">
          <span>
            <i>The weekend,</i>
          </span>
          <span>
            <i>hour by hour.</i>
          </span>
        </h1>
        <p className="es-hero__lede">{schedule.note}</p>
      </section>

      {schedule.days.map((day) => (
        <section className="es-day" key={day.label}>
          <div className="es-day__head">
            {/* An h2: the page already has its h1 above, and a schedule whose
                days outrank the page title reads as two pages stapled
                together to anything navigating by heading. */}
            <h2>{day.label}</h2>
            <span>{day.date}</span>
          </div>

          <ul className="es-tl">
            {day.items.map((item, i) => (
              <li
                key={item.time}
                data-kind={item.kind}
                style={{ "--i": i } as React.CSSProperties}
              >
                <span className="es-tl__t">{item.time}</span>
                <span className="es-tl__rail" aria-hidden="true">
                  <span className="es-tl__dot" />
                </span>
                <span className="es-tl__b">
                  <strong>{item.title}</strong>
                  {item.detail ? <span>{item.detail}</span> : null}
                </span>
              </li>
            ))}
          </ul>
        </section>
      ))}

      <section className="es-sec">
        <SectionHead
          kicker="Before you come"
          heading="What to bring."
          lede="A laptop and its charger, something to sleep in if you plan to stay, and a student ID. Everything else, including every meal, is covered. If there is something you need to make the weekend work for you, write to us and we will sort it out."
        />

        <div className="es-after">
          <a
            className="es-btn es-btn--ghost"
            href={`mailto:${contactEmail}?subject=${encodeURIComponent(
              `Question about MorganHacks ${siteConfig.year}`,
            )}`}
          >
            Ask us anything
            <ArrowRight />
          </a>
        </div>
      </section>
    </Frame>
  );
}
