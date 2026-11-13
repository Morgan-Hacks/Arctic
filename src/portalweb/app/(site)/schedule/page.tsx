import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { siteConfig } from "@/site.config";
import { HEIST_THEME, isOn } from "@/lib/features";
import { HeistNav, HeistFoot, HeistTicker, MlhBadge } from "../../../components/heist-site/chrome";
import { HeistRain } from "../../../components/heist-site/rain";

const { schedule, contactEmail } = siteConfig;

export const metadata: Metadata = {
  title: `Schedule – MorganHacks ${siteConfig.year}`,
  description:
    "The provisional run of show for MorganHacks 2027 at Morgan State University: doors, opening ceremony, workshops, meals, submissions and judging.",
};

/** The word under each dot, so the dot's colour is never the only carrier. */
const KIND_LABEL: Record<string, string> = {
  open: "Doors",
  main: "Main",
  side: "Optional",
  meal: "Food",
  stop: "Deadline",
};

export default function SchedulePage() {
  // The whole site lives behind enable_heist_theme_feature. With the flag off
  // this route does not exist at all rather than rendering an unstyled page --
  // a feature that is off should be indistinguishable from one that was never
  // built, and a 404 is what "never built" looks like from outside.
  if (!isOn(HEIST_THEME)) notFound();

  return (
    <>
      <MlhBadge />
      <HeistNav current="schedule" />
      <HeistTicker />
      <HeistRain />

      <main>
        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// The plan</span>
            <h1 className="heist-h2">How the weekend runs.</h1>
            <p className="heist-lede">
              Twenty-four hours from the opening brief to the last demo. You can
              arrive late, sleep, leave and come back. The only hard time on this
              page is when submissions close.
            </p>
          </div>

          {/*
            The provisional banner, rendered from site.config's own flag rather
            than typed here. Nothing on this schedule has been agreed with the
            venue, and a run of show that looks settled is one people book
            travel around. Take the flag off in site.config.ts when the times
            are real and this disappears by itself.
          */}
          {schedule.provisional ? (
            <div className="heist-notice" role="note">
              <span>
                <b>Not final</b>
                {schedule.note} Nothing here is booked yet. Check back closer to
                April, or{" "}
                <a href={`mailto:${contactEmail}`}>ask us</a> if you are planning
                travel.
              </span>
            </div>
          ) : null}

          <div style={{ marginTop: "clamp(32px, 5vh, 56px)" }}>
            {schedule.days.map((day) => (
              <section className="heist-day" key={day.label}>
                <div className="heist-day__head">
                  <h2>{day.label}</h2>
                  <span>{day.date}</span>
                </div>

                <ul className="heist-run">
                  {day.items.map((item) => (
                    <li key={`${day.label}-${item.time}`} data-kind={item.kind}>
                      <span className="heist-run__time">{item.time}</span>
                      <span className="heist-run__rail" aria-hidden="true">
                        <span className="heist-run__dot" />
                      </span>
                      <span className="heist-run__body">
                        <strong>{item.title}</strong>
                        {item.detail ? <span>{item.detail}</span> : null}
                        <span className="heist-run__tag">
                          {KIND_LABEL[item.kind] ?? item.kind}
                        </span>
                      </span>
                    </li>
                  ))}
                </ul>
              </section>
            ))}
          </div>
        </section>

        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// Before you come</span>
            <h2 className="heist-h2">What to bring.</h2>
          </div>
          <div className="heist-grid">
            <article className="heist-card">
              <span className="heist-card__index">01</span>
              <h3>A laptop and charger</h3>
              <p>
                And a power strip if you have one. There are never enough
                sockets, and the person you lend it to becomes your teammate.
              </p>
            </article>
            <article className="heist-card">
              <span className="heist-card__index">02</span>
              <h3>Student ID</h3>
              <p>
                Check-in needs it. Any university, not just Morgan State.
              </p>
            </article>
            <article className="heist-card">
              <span className="heist-card__index">03</span>
              <h3>Something to sleep on</h3>
              <p>
                The room stays open overnight. A blanket and a pillow make the
                difference between a nap and a bad morning.
              </p>
            </article>
          </div>
        </section>
      </main>

      <HeistFoot />
    </>
  );
}
