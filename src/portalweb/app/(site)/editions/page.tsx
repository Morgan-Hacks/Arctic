import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { siteConfig } from "@/site.config";
import { EVENT_SITE, isOn } from "@/lib/features";
import { Frame, SectionHead } from "../../../components/event-site/frame";
import { ArrowUpRight } from "../../../components/event-site/icons";

const { editions, contactEmail } = siteConfig;

export const metadata: Metadata = {
  title: `Five editions — MorganHacks ${siteConfig.year}`,
  description:
    "Every MorganHacks since 2023, each one still online on its own subdomain. 2027 is the fifth.",
};

/**
 * The archive.
 *
 * Each card links to that edition's own site, which is still served on its own
 * subdomain. Dates are only shown where the edition's own page still states
 * one: 2025's never published a date, and the card says that in those words
 * instead of guessing, because there is nobody left who would catch a wrong
 * one. The current edition is a card too, and it is not a link, because
 * linking this page to the page it is on is a dead end dressed as a door.
 */
export default function EditionsPage() {
  if (!isOn(EVENT_SITE)) notFound();

  return (
    <Frame current="editions">
      <section>
        <span className="es-rule" aria-hidden="true" />
        <p className="es-hero__eyebrow">2023 to 2027</p>
        <h1 className="es-hero__title">
          <span>
            <i>Five editions,</i>
          </span>
          <span>
            <i>all still online.</i>
          </span>
        </h1>
        <p className="es-hero__lede">
          Every MorganHacks has kept its own site. Nothing has been taken down
          and nothing has been rewritten, so each one is still the page its
          organizers shipped that April.
        </p>
      </section>

      <section className="es-sec">
        <SectionHead
          kicker="The record"
          heading="One April at a time."
          lede="Four have run and the fifth is being built. Dates come from each edition's own site, so where one never published them this page says so rather than filling in a guess."
        />

        <ul className="es-years">
          {editions.map((e, i) => {
            const style = { "--i": i } as React.CSSProperties;

            // The dateless case is split in two because the two mean entirely
            // different things: 2027 has no date yet, and 2025 had one that
            // nobody wrote down.
            const dates =
              e.dates ?? (e.href === null ? "April 2027, dates to come" : "Dates not published");

            if (e.href === null) {
              return (
                <li key={e.year}>
                  <div className="es-yr es-yr--current" style={style}>
                    <span className="es-yr__y">{e.year}</span>
                    <span className="es-yr__b">
                      <strong>{dates}</strong>
                      <span>{e.note}</span>
                    </span>
                    <span className="es-yr__go">This one</span>
                  </div>
                </li>
              );
            }

            return (
              <li key={e.year}>
                <a
                  className="es-yr"
                  style={style}
                  href={e.href}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <span className="es-yr__y">{e.year}</span>
                  <span className="es-yr__b">
                    <strong>{dates}</strong>
                    <span>{e.note}</span>
                  </span>
                  <span className="es-yr__go">
                    Visit
                    <ArrowUpRight />
                  </span>
                </a>
              </li>
            );
          })}
        </ul>
      </section>

      <section className="es-sec">
        <SectionHead
          kicker="Were you there"
          heading="We are missing photographs."
          lede="The earlier editions were better documented in people's camera rolls than on their websites. If you have pictures, a project you built, or a story from one of the first four, we would like them for the fifth."
        />

        <div className="es-after">
          <a
            className="es-btn es-btn--ghost"
            href={`mailto:${contactEmail}?subject=${encodeURIComponent(
              "Something from an earlier MorganHacks",
            )}`}
          >
            Send it over
          </a>
        </div>
      </section>
    </Frame>
  );
}
