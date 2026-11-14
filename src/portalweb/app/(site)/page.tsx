import { ArrowUpRight, Frame, SectionHead } from "@/components/event-site/frame";
import { EVENT_SITE, isOn } from "@/lib/features";
import { siteConfig } from "@/site.config";

const { announcement, contactEmail, socials, mlh, edition, facts, weekend, editions, sponsors } =
  siteConfig;

/**
 * The public page between editions.
 *
 * Three lines of type and a footer. There is one fact to communicate and no
 * date to put a countdown on, so the page is the sentence and nothing else --
 * no scene, no board, no client components and no state.
 *
 * Still the page `/` renders whenever enable_event_site_feature is off, which
 * is everywhere except staging. Untouched by the event site below it.
 */
function Holding() {
  return (
    <div className="screen">
      <header className="topline">
        <p className="wordmark">MorganHacks</p>

        {/* MLH require the badge hotlinked from their host and pinned to a top
            corner. A plain <img> because it is an SVG, so next/image would add
            a remote-pattern config for nothing. */}
        <a className="mlh" href={mlh.href} target="_blank" rel="noopener noreferrer">
          <img
            src={mlh.badge}
            alt={`Major League Hacking ${mlh.season} Hackathon Season`}
            width={80}
            height={139}
          />
        </a>
      </header>

      <main className="copy">
        {/* Two rules, opening and closing the block. They carry no information,
            which is why they are rules and not words: they are what makes the
            three lines between them read as one object rather than a stack. */}
        <span className="mark" aria-hidden="true" />

        <p className="eyebrow">{announcement.eyebrow}</p>
        <h1 className="headline">{announcement.headline}</h1>
        <p className="line">{announcement.line}</p>

        <span className="mark" aria-hidden="true" />
      </main>

      <footer className="foot">
        <nav className="links" aria-label="MorganHacks on social media">
          <a href={socials.instagram} target="_blank" rel="noopener noreferrer">
            Instagram
          </a>
          <a href={socials.linkedin} target="_blank" rel="noopener noreferrer">
            LinkedIn
          </a>
          <a href={socials.tiktok} target="_blank" rel="noopener noreferrer">
            TikTok
          </a>
        </nav>

        <span className="foot__sep" aria-hidden="true">
          ·
        </span>

        <a href={`mailto:${contactEmail}`}>{contactEmail}</a>
      </footer>
    </div>
  );
}

/**
 * The five editions, as a ledger.
 *
 * Shared with /editions, which sets the same list without the surrounding
 * section. The year is a display numeral hung on the left and everything else
 * sits beside it, so five entries read as one continuous record rather than
 * as five cards.
 */
export function EditionLedger() {
  return (
    <ol className="es-ledger">
      {editions.map((entry) => (
        <li
          key={entry.year}
          className={`es-year${entry.href === null ? " es-year--now" : ""}`}
        >
          <div className="es-year__num">{entry.year}</div>

          <div className="es-year__body">
            {/* 2025 never published its dates. Saying so is better than a gap
                the reader has to interpret, and better than inventing one. */}
            {entry.dates === null ? null : (
              <span className="es-year__dates es-label">{entry.dates}</span>
            )}
            <p className="es-year__note">{entry.note}</p>
          </div>

          {entry.href === null ? null : (
            <a className="es-year__link" href={entry.href}>
              <span>Its site</span>
              <ArrowUpRight />
            </a>
          )}
        </li>
      ))}
    </ol>
  );
}

function EventHome() {
  return (
    <Frame current="home">
      <section className="es-hero">
        <p className="es-hero__eyebrow es-label">{edition.eyebrow}</p>
        <h1 className="es-hero__title">{edition.headline}</h1>
        <hr className="es-hero__rule" />
        <p className="es-hero__deck">{edition.deck}</p>

        <div className="es-hero__actions">
          {/* No apply button, because there is nowhere for it to go. A control
              that opens nothing is worse than its absence: it is the thing the
              holding page exists to avoid, which is a dead form left up. */}
          <p className="es-note">
            Applications are not open yet. The dates are announced here first.
          </p>
        </div>

        <dl className="es-facts">
          {facts.map((fact) => (
            <div key={fact.label}>
              <dt className="es-label">{fact.label}</dt>
              <dd>{fact.value}</dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="es-sec">
        <SectionHead folio="01" kicker="The event" heading="What it is." />
        <div className="es-prose es-prose--two es-prose--drop">
          {edition.lede.map((para) => (
            <p key={para.slice(0, 24)}>{para}</p>
          ))}
        </div>
      </section>

      <section className="es-sec">
        <SectionHead
          folio="02"
          kicker="The weekend"
          heading="Saturday to Sunday."
          lede="The running order is not set yet. The shape of it has been the same for five years."
        />
        <div className="es-weekend">
          {weekend.map((part) => (
            <div key={part.heading}>
              <h3>{part.heading}</h3>
              <p className="es-weekend__body">{part.body}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="es-sec">
        <SectionHead
          folio="03"
          kicker="The record"
          heading="One April at a time."
          lede="Every edition had its own site, and every one of them is still up."
        />
        <EditionLedger />
      </section>

      <section className="es-sec">
        <SectionHead folio="04" kicker="Sponsors" heading="It is free because it is paid for." />
        <div className="es-closing">
          <p>{sponsors.letter[1]}</p>
          <p>
            <a className="es-btn" href="/sponsors">
              Read the prospectus
            </a>
          </p>
        </div>
      </section>
    </Frame>
  );
}

export default function Home() {
  return isOn(EVENT_SITE) ? <EventHome /> : <Holding />;
}
