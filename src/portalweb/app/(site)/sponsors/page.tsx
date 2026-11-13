import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { siteConfig } from "@/site.config";
import { EVENT_SITE, isOn } from "@/lib/features";
import { Frame, SectionHead } from "../../../components/event-site/frame";
import { ArrowRight, Check } from "../../../components/event-site/icons";

const { sponsors, contactEmail, edition } = siteConfig;

export const metadata: Metadata = {
  title: `Sponsors — MorganHacks ${siteConfig.year}`,
  description:
    "Sponsorship tiers for MorganHacks 2027, the fifth edition of the student hackathon at Morgan State University.",
};

/**
 * The sponsor prospectus.
 *
 * The wall of logos at the bottom is empty, and it says why in words. Nothing
 * has been confirmed for 2027; dressing the page with last year's partners
 * would claim a relationship nobody has agreed to this year, which is a real
 * problem rather than a design one. An honest empty state also does something
 * a stale wall cannot, which is make the first slot look available.
 */
export default function SponsorsPage() {
  if (!isOn(EVENT_SITE)) notFound();

  return (
    <Frame current="sponsors">
      <section>
        <span className="es-rule" aria-hidden="true" />
        <p className="es-hero__eyebrow">Sponsorship</p>
        <h1 className="es-hero__title">
          <span>
            <i>Put your name</i>
          </span>
          <span>
            <i>on the fifth one.</i>
          </span>
        </h1>
        <p className="es-hero__lede">{sponsors.pitch.line}</p>

        <div className="es-hero__actions">
          <a
            className="es-btn es-btn--primary"
            href={`mailto:${contactEmail}?subject=${encodeURIComponent(
              `Sponsoring MorganHacks ${siteConfig.year}`,
            )}`}
          >
            Talk to us
            <ArrowRight />
          </a>
        </div>
      </section>

      <section className="es-sec">
        <SectionHead
          kicker="Tiers"
          heading="What each level gets."
          lede="Amounts are the asking price rather than a fixed menu. If something on a higher tier matters to you and the number does not work, say so in the email and we will find the shape that does."
        />

        <div className="es-tiers">
          {sponsors.tiers.map((tier) => (
            <article className="es-tier" key={tier.name}>
              <p className="es-tier__name">{tier.name}</p>
              <p className="es-tier__amt">{tier.amount}</p>
              <ul>
                {tier.benefits.map((b) => (
                  <li key={b}>
                    <Check />
                    <span>{b}</span>
                  </li>
                ))}
              </ul>
            </article>
          ))}
        </div>
      </section>

      <section className="es-sec">
        <SectionHead
          kicker="Why it is worth it"
          heading="Who is in the room."
          lede={`A few hundred students from Morgan State and the universities around Baltimore, most of them in computing, engineering or design, and most of them looking for an internship. This is the ${edition.ordinal.toLowerCase()} edition, which means the people who came to the first one are graduating now.`}
        />

        <p className="es-well">
          Sponsoring a hackathon is the cheapest way to spend a weekend watching
          people solve problems before you interview them about it.
        </p>
      </section>

      <section className="es-sec">
        <SectionHead kicker="This year" heading="Confirmed sponsors." />

        {sponsors.confirmed.length > 0 ? (
          <ul className="es-years">
            {sponsors.confirmed.map((s) => (
              <li key={s.name}>
                <a
                  className="es-yr"
                  href={s.href}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <span className="es-yr__b">
                    <strong>{s.name}</strong>
                  </span>
                </a>
              </li>
            ))}
          </ul>
        ) : (
          <div className="es-empty">
            <p>
              Nobody yet. Sponsors for {siteConfig.year} are still being
              confirmed, so this space is empty on purpose rather than showing
              you last year&rsquo;s partners.
            </p>
            <a
              className="es-btn es-btn--primary"
              href={`mailto:${contactEmail}?subject=${encodeURIComponent(
                `Sponsoring MorganHacks ${siteConfig.year}`,
              )}`}
            >
              Be the first
              <ArrowRight />
            </a>
          </div>
        )}
      </section>
    </Frame>
  );
}
