import { Fragment } from "react";
import { siteConfig } from "@/site.config";

const { year, announcement, contactEmail, socials, mlh } = siteConfig;

/**
 * The public page between editions.
 *
 * Eleven words of message and four facts, none of which are known yet. The
 * board is the design: a label with "To be announced" beside it reads as a slot
 * waiting to be filled, where a bare sentence reads as a page that gave up.
 *
 * No scene, no canvas and nothing client-side. The previous version carried
 * three absolutely-positioned SVG layers and a countdown that ran on an
 * interval; this one is static markup and two CSS gradients.
 */
export default function Home() {
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
        <p className="status">
          {/* Decorative: the words beside it already say what it means. */}
          <span className="status__dot" aria-hidden="true" />
          <span>
            <span className="status__year">MorganHacks {year}</span> · In planning
          </span>
        </p>

        <h1 className="headline">
          {announcement.headline} <em>{announcement.headlineEmphasis}</em>.
        </h1>

        {/*
          A Fragment per row, not a wrapping <div style={{ display: "contents" }}>.
          `display: contents` makes the pair lay out as grid items, but it does
          nothing for selector matching -- `.board > dt` needs dt to be a direct
          DOM child, and inside a wrapper it is not. The grid still looked right
          and every other board rule silently stopped applying: no rules between
          rows, no padding, no monospace. It reads as a styling problem and it is
          a markup one.
        */}
        <dl className="board">
          {announcement.board.map((row) => (
            <Fragment key={row.label}>
              <dt>{row.label}</dt>
              <dd>{row.value}</dd>
            </Fragment>
          ))}
        </dl>
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

        <p>
          <a href={`mailto:${contactEmail}`}>{contactEmail}</a>
        </p>
      </footer>
    </div>
  );
}
