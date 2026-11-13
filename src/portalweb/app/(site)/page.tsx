import { siteConfig } from "@/site.config";
import { EVENT_SITE, isOn } from "@/lib/features";
import { Frame, SectionHead } from "../../components/event-site/frame";
import { ArrowRight, ArrowUpRight } from "../../components/event-site/icons";

const { announcement, contactEmail, socials, mlh, edition, editions, facts, howItWorks, schedule, sponsors } =
  siteConfig;

/**
 * The public page between editions.
 *
 * Three lines of type and a footer. There is one fact to communicate and no
 * date to put a countdown on, so the page is the sentence and nothing else --
 * no scene, no board, no client components and no state.
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
 * The five editions, as the hero object.
 *
 * The anniversary is a claim about a count, so the thing that carries it is
 * the count itself with its workings shown: five rows, oldest first, the
 * current one picked out. The row with no date says so, because 2025's own
 * site never published one and filling it in from memory is how an archive
 * starts lying.
 */
function EditionLedger() {
  return (
    <aside className="es-ed" aria-label="Every edition of MorganHacks">
      <p className="es-ed__num">05</p>
      <p className="es-ed__cap">{edition.ordinal} edition</p>

      <ul className="es-ed__list">
        {editions.map((e, i) => (
          <li
            className="es-ed__row"
            key={e.year}
            data-current={e.href === null}
            style={{ "--i": i } as React.CSSProperties}
          >
            <span className="es-ed__y">{e.year}</span>
            <span className="es-ed__d">
              {e.dates ?? (e.href === null ? "April, dates to come" : "Dates not published")}
            </span>
          </li>
        ))}
      </ul>

      <a className="es-ed__more" href="/editions">
        All five editions
        <ArrowUpRight />
      </a>
    </aside>
  );
}

/**
 * The landing page.
 *
 * Above the fold it answers what this is, where, when and what it costs, and
 * it does that as a labelled list rather than inside a paragraph because that
 * is the shape people scan. Each section below is a short answer plus the way
 * into the page that answers it properly, so nothing here is a worse copy of
 * /schedule or /sponsors.
 *
 * There is deliberately no countdown. `event.startIso` in site.config.ts is
 * marked a placeholder -- April 2027 is all that has been published -- and a
 * clock ticking down to an invented hour is a made-up fact rendered in the
 * most confident component on the page.
 */
function EventHome() {
  // The first five items of Saturday, as a taste of the run of show. Both days
  // in full live on /schedule; repeating them here would make this page a
  // worse version of that one.
  const preview = schedule.days[0].items.slice(0, 5);

  return (
    <Frame current="home">
      <section className="es-hero">
        <div>
          <span className="es-rule" aria-hidden="true" />
          <p className="es-hero__eyebrow">{edition.eyebrow}</p>

          <h1 className="es-hero__title">
            <span>
              <i>{edition.headlineTop}</i>
            </span>
            <span>
              <i>{edition.headlineBottom}</i>
            </span>
          </h1>

          <p className="es-hero__lede">{edition.lede}</p>

          <div className="es-hero__actions">
            <a
              className="es-btn es-btn--primary"
              href={`mailto:${contactEmail}?subject=${encodeURIComponent(
                `Tell me when MorganHacks ${siteConfig.year} opens`,
              )}`}
            >
              Tell me when it opens
              <ArrowRight />
            </a>
            <a className="es-btn es-btn--ghost" href="/schedule">
              See the weekend
            </a>
          </div>

          <dl className="es-facts">
            {facts.map((f) => (
              <div key={f.label}>
                <dt>{f.label}</dt>
                <dd>{f.value}</dd>
              </div>
            ))}
          </dl>
        </div>

        <EditionLedger />
      </section>

      <section className="es-sec">
        <SectionHead
          kicker="How it works"
          heading="Twenty-four hours, start to finish."
          lede="Never been to a hackathon? It is simpler than it sounds. You turn up with nothing, you leave with something you built, and the rest is food, help and time."
        />

        <ol className="es-steps">
          {howItWorks.map((step, i) => (
            <li key={step.title}>
              <span className="es-steps__n" aria-hidden="true">
                {String(i + 1).padStart(2, "0")}
              </span>
              <div>
                <h3>{step.title}</h3>
                <p>{step.body}</p>
              </div>
            </li>
          ))}
        </ol>
      </section>

      <section className="es-sec">
        <SectionHead
          kicker="The weekend"
          heading="How Saturday starts."
          lede={schedule.note}
        />

        <ul className="es-tl">
          {preview.map((item, i) => (
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

        <p className="es-after">
          <a className="es-btn es-btn--ghost" href="/schedule">
            Both days in full
            <ArrowRight />
          </a>
        </p>
      </section>

      <section className="es-sec">
        <SectionHead
          kicker="Sponsors"
          heading={sponsors.pitch.heading}
          lede={sponsors.pitch.line}
        />

        <ul className="es-strip">
          {sponsors.tiers.map((tier) => (
            <li key={tier.name}>
              <span className="es-strip__n">{tier.name}</span>
              <span className="es-strip__a">{tier.amount}</span>
            </li>
          ))}
        </ul>

        <div className="es-after">
          <a className="es-btn es-btn--primary" href="/sponsors">
            What each level gets
            <ArrowRight />
          </a>
          <a
            className="es-btn es-btn--ghost"
            href={`mailto:${contactEmail}?subject=${encodeURIComponent(
              `Sponsoring MorganHacks ${siteConfig.year}`,
            )}`}
          >
            Start a conversation
          </a>
        </div>
      </section>

      <section className="es-sec">
        <SectionHead
          kicker="Next"
          heading="Applications are not open yet."
          lede="There is nothing to sign up for today. When applications open you will want a few days' notice rather than to find out from somebody else, so write to us and that is what you will get."
        />

        <div className="es-after">
          <a
            className="es-btn es-btn--primary"
            href={`mailto:${contactEmail}?subject=${encodeURIComponent(
              `Tell me when MorganHacks ${siteConfig.year} opens`,
            )}`}
          >
            Tell me when it opens
            <ArrowRight />
          </a>
        </div>
      </section>
    </Frame>
  );
}

export default function Home() {
  return isOn(EVENT_SITE) ? <EventHome /> : <Holding />;
}
