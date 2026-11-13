import { siteConfig } from "@/site.config";
import { HEIST_THEME, isOn } from "@/lib/features";
import { HeistNav, HeistFoot, HeistTicker, MlhBadge } from "../../components/heist-site/chrome";
import { HeistRain } from "../../components/heist-site/rain";
import { HeistVault } from "../../components/heist-site/vault";
import { Countdown } from "../../components/heist/countdown";

const { announcement, contactEmail, socials, mlh, anniversary, archive, schedule, event } =
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

/** What the weekend actually is, for somebody who has never been to one. */
const BRIEF = [
  {
    title: "Pick a job",
    body: "A theme, a sponsor's challenge, or whatever you have been meaning to build. You get the brief at the opening ceremony and 24 hours after it.",
  },
  {
    title: "Pull a crew",
    body: "Teams of up to four. Come with one or find one in the first hour. Most people arrive alone and leave with a team they keep.",
  },
  {
    title: "Make the drop",
    body: "Submit before the deadline, demo it to a judge at your table, and show the room what you got away with.",
  },
];

/**
 * The Heist landing page.
 *
 * The one thing it has to do is answer, above the fold, what this is, when it
 * is and what it costs -- those three sit under the buttons as labelled facts
 * rather than inside a paragraph, because that is the shape people scan.
 * Everything below the fold is detail for somebody who has decided to keep
 * reading.
 */
function HeistHome() {
  // The first morning, shown as a taste of the run of show. The schedule page
  // has both days in full; repeating them here would make the landing page a
  // worse copy of it.
  const preview = schedule.days[0].items.slice(0, 4);

  return (
    <>
      <MlhBadge />
      <HeistNav current="home" />
      <HeistTicker />
      <HeistRain />

      <main>
        <section className="heist-shell heist-hero">
          <div className="heist-hero__copy">
            <span className="heist-eyebrow">// {anniversary.eyebrow}</span>

            <h1 className="heist-hero__title">
              {anniversary.headlineTop}
              <em>{anniversary.headlineBottom}</em>
            </h1>

            <p className="heist-lede">
              MorganHacks returns to Morgan State University in April 2027 for its
              fifth year. One weekend, a room full of crews, and 24 hours to build
              something that did not exist on Friday.
            </p>

            <div className="heist-hero__actions">
              <a
                className="heist-button heist-button--primary"
                href={`mailto:${contactEmail}?subject=Tell%20me%20when%20MorganHacks%20${siteConfig.year}%20opens`}
              >
                Get on the list
              </a>
              <a className="heist-button heist-button--ghost" href="/schedule">
                See the weekend
              </a>
            </div>

            {/* Where, when and what it costs. The three questions somebody has
                before they read anything else, answered as labelled facts so
                they can be found without reading a paragraph. */}
            <dl className="heist-hero__facts">
              <div>
                <dt>Where</dt>
                <dd>Morgan State University</dd>
              </div>
              <div>
                <dt>When</dt>
                <dd>April 2027, dates to come</dd>
              </div>
              <div>
                <dt>Entry</dt>
                <dd>Free for students</dd>
              </div>
            </dl>
          </div>

          <div className="heist-hero__art">
            <HeistVault />
            {event.startIso ? <Countdown startIso={event.startIso} /> : null}
          </div>
        </section>

        <section className="heist-shell heist-section" id="brief">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// The brief</span>
            <h2 className="heist-h2">Twenty-four hours, start to finish.</h2>
            <p className="heist-lede">
              Never been to a hackathon? It is simpler than it sounds. You arrive
              with nothing, you leave with something you built, and everything in
              between is food, help and time.
            </p>
          </div>

          <div className="heist-grid">
            {BRIEF.map((card, i) => (
              <article className="heist-card" key={card.title}>
                <span className="heist-card__index">
                  {String(i + 1).padStart(2, "0")}
                </span>
                <h3>{card.title}</h3>
                <p>{card.body}</p>
              </article>
            ))}
          </div>
        </section>

        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// The record</span>
            <h2 className="heist-h2">{anniversary.heading}</h2>
            <p className="heist-lede">{anniversary.line}</p>
          </div>

          <ul className="heist-years">
            {archive.map((edition) => (
              <li key={edition.year}>
                <a
                  className="heist-year"
                  href={edition.href}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <span className="heist-year__num">{edition.year}</span>
                  <span className="heist-year__body">
                    <strong>{edition.dates ?? "Dates not recorded"}</strong>
                    <span>{edition.note}</span>
                  </span>
                  <span className="heist-year__go">Visit site →</span>
                </a>
              </li>
            ))}
            <li>
              <div className="heist-year heist-year--current">
                <span className="heist-year__num">{siteConfig.year}</span>
                <span className="heist-year__body">
                  <strong>April 2027, dates to come</strong>
                  <span>The fifth. You are looking at it.</span>
                </span>
                <span className="heist-year__go">This one</span>
              </div>
            </li>
          </ul>
        </section>

        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// The plan</span>
            <h2 className="heist-h2">How the weekend runs.</h2>
            <p className="heist-lede">{schedule.note}</p>
          </div>

          <ul className="heist-run">
            {preview.map((item) => (
              <li key={item.time} data-kind={item.kind}>
                <span className="heist-run__time">{item.time}</span>
                <span className="heist-run__rail" aria-hidden="true">
                  <span className="heist-run__dot" />
                </span>
                <span className="heist-run__body">
                  <strong>{item.title}</strong>
                  {item.detail ? <span>{item.detail}</span> : null}
                </span>
              </li>
            ))}
          </ul>

          <p style={{ marginTop: "28px" }}>
            <a className="heist-button heist-button--ghost" href="/schedule">
              Both days in full
            </a>
          </p>
        </section>

        <section className="heist-shell heist-section">
          <div className="heist-section__head">
            <span className="heist-eyebrow">// The backers</span>
            <h2 className="heist-h2">{siteConfig.sponsors.pitch.heading}</h2>
            <p className="heist-lede">{siteConfig.sponsors.pitch.line}</p>
          </div>
          <div className="heist-hero__actions">
            <a className="heist-button heist-button--primary" href="/sponsors">
              See the tiers
            </a>
            <a
              className="heist-button heist-button--ghost"
              href={`mailto:${contactEmail}?subject=Sponsoring%20MorganHacks%20${siteConfig.year}`}
            >
              Talk to us
            </a>
          </div>
        </section>
      </main>

      <HeistFoot />
    </>
  );
}

export default function Home() {
  return isOn(HEIST_THEME) ? <HeistHome /> : <Holding />;
}
