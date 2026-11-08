import { siteConfig } from "@/site.config";
import { Countdown } from "./countdown";
import { Aurora, Constellation, Contours, STAR } from "./scene";

const { year, recruiting, holding, organizerFormUrl, deadline, contactEmail, socials, mlh } =
  siteConfig;

export default function Home() {
  return (
    <div className="screen">
      <Contours />
      <Constellation />
      <Aurora />

      {/* MLH require the badge hotlinked from their host and pinned to a top
          corner. Rendered as a plain <img> — it is an SVG, so next/image would
          add a remote-pattern config for nothing. */}
      <a
        className="mlh"
        href={mlh.href}
        target="_blank"
        rel="noopener noreferrer"
      >
        <img
          src={mlh.badge}
          alt={`Major League Hacking ${mlh.season} Hackathon Season`}
          width={80}
          height={139}
        />
      </a>

      <header className="topline">
        <span className="wordmark">
          <svg
            className="wordmark__star"
            viewBox="0 0 24 24"
            aria-hidden="true"
            focusable="false"
          >
            <path d={STAR} />
          </svg>
          <span className="wordmark__text">MorganHacks</span>
        </span>
      </header>

      <main className="copy">
        {/* Grouped so the three blocks can spread down the screen while each
            one stays internally tight. */}
        <div className="copy__intro">
          <p className="eyebrow">
            <span>
              MorganHacks {year}
              {recruiting ? " · Organizer applications" : null}
            </span>
          </p>

          <h1 className="headline">
            {recruiting ? (
              <>
                Think you could <em>pull this off</em>?
              </>
            ) : (
              <>
                {holding.headline} <em>{holding.headlineEmphasis}</em>.
              </>
            )}
          </h1>
        </div>

        <p className="dare">
          {recruiting ? "Only one way to find out." : holding.line}
        </p>

        <div className="copy__act">
          {/*
            While the drive is off there is no call to action, and that is the
            point of the switch rather than an oversight. A button is a promise
            that something is waiting on the other side of it, and for eight
            weeks this one led to a form that had closed. Nothing here is better
            than something that is not true.
          */}
          {recruiting ? (
            <>
              <div className="actions">
                <a
                  className="cta"
                  href={organizerFormUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Apply now
                  <span className="cta__arrow" aria-hidden="true">
                    →
                  </span>
                </a>
              </div>

              <Countdown
                iso={deadline.iso}
                date={deadline.date}
                time={deadline.time}
              />
            </>
          ) : (
            <p className="announce">{holding.follow}</p>
          )}

          <p className="contact">
            Questions? <a href={`mailto:${contactEmail}`}>{contactEmail}</a>
          </p>
        </div>
      </main>

      <nav className="links">
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
    </div>
  );
}
