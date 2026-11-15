import { Fragment } from "react";

import { Torch } from "@/components/event-site/motion";
import { siteConfig } from "@/site.config";

/**
 * The masthead, the footer and the section head, shared by the three pages.
 *
 * Server components with no state and no handlers, and the links are plain
 * anchors rather than next/link on purpose: three static pages do not need a
 * client router, and prefetching would ship a runtime to a site whose whole
 * job is to be read. A full navigation between them is a few kilobytes of
 * HTML.
 */

/** The pages, in the order the masthead lists them. */
const NAV = [
  { id: "home", href: "/", label: "Home" },
  { id: "sponsors", href: "/sponsors", label: "Sponsors" },
  { id: "editions", href: "/editions", label: "Editions" },
] as const;

export type NavId = (typeof NAV)[number]["id"];

/**
 * A line of type whose words arrive one after the next.
 *
 * Split on the server into one inline-block per word, each carrying its index
 * as a custom property; the CSS turns that index into a delay. No JavaScript,
 * no measuring, no layout shift, and the sentence is still one readable string
 * in the HTML for anything that is not a browser.
 *
 * Words rather than letters. Per-letter staggers read as a gimmick at this
 * size and, worse, they put each character in its own box, so a screen reader
 * and a find-in-page both lose the word.
 *
 * The spaces are text nodes between the spans, not characters inside them: a
 * space at the end of an inline-block is trimmed, and putting them inside
 * runs the whole headline together into one word.
 */
export function SplitWords({ text }: { text: string }) {
  const words = text.split(" ");

  return (
    <span className="es-split">
      {words.map((word, i) => (
        <Fragment key={`${i}-${word}`}>
          <span className="es-split__w" style={{ "--i": i } as React.CSSProperties}>
            {word}
          </span>
          {i < words.length - 1 ? " " : null}
        </Fragment>
      ))}
    </span>
  );
}

/**
 * The band of statements that scrolls across under the hero.
 *
 * Two identical runs in one track, moved by exactly half its width and then
 * snapped back: the second run is where the first one was, so there is no
 * seam and no point at which the strip is empty. It is the cheapest marquee
 * there is -- one composited transform, no JavaScript, no clones made at
 * runtime -- and the duplicate is hidden from assistive technology so the
 * same eight phrases are not read out twice.
 *
 * The separator is a drawn diamond rather than a typed character, so nothing
 * here is a middle dot pretending to be punctuation.
 */
export function Ticker({ items }: { items: readonly string[] }) {
  const run = (hidden: boolean) => (
    <div className="es-ticker__run" aria-hidden={hidden || undefined}>
      {items.map((item) => (
        <span key={item} className="es-ticker__item">
          {item}
        </span>
      ))}
    </div>
  );

  return (
    <div className="es-ticker">
      <div className="es-ticker__track">
        {run(false)}
        {run(true)}
      </div>
    </div>
  );
}

export function ArrowUpRight() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.5} aria-hidden="true">
      <path d="M7 17 17 7M17 7H9m8 0v8" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function Check() {
  return (
    <svg
      className="es-matrix__check"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      aria-hidden="true"
    >
      <path d="m5 13 4 4L19 7" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

/**
 * A section head: the number and kicker in the margin, the heading beside it.
 *
 * `folio` is passed in rather than counted, because the sections are written
 * out in each page's JSX and a component that numbered itself would have to
 * know the order of its siblings.
 */
export function SectionHead({
  folio,
  kicker,
  heading,
  lede,
}: {
  folio: string;
  kicker: string;
  heading: string;
  lede?: string;
}) {
  return (
    <header className="es-head">
      <div className="es-head__meta">
        <div className="es-head__folio">{folio}</div>
        <div className="es-label">{kicker}</div>
      </div>
      <div>
        <h2 className="es-head__title">{heading}</h2>
        {lede ? <p className="es-head__lede">{lede}</p> : null}
      </div>
    </header>
  );
}

export function Frame({ current, children }: { current: NavId; children: React.ReactNode }) {
  return (
    <div className="event-site">
      <a className="es-skip" href="#main">
        Skip to content
      </a>

      {/* Outside the shell, because a sticky bar that is only as wide as the
          content column has two visible edges with the background showing
          past them. It carries its own inner column instead. */}
      {/* The pool of light that follows the pointer. First child, because it
          is scenery: it sits on the same layer as the mesh and the grain,
          under everything that is actually read. */}
      <Torch />

      <header className="es-masthead">
        {/* How far down the page you are, as a hot line under the bar. Driven
            by a scroll timeline in CSS, so it costs no JavaScript at all and
            simply does not appear in a browser that cannot do that. */}
        <span className="es-progress" aria-hidden="true" />

        <div className="es-masthead__inner">
          <a className="es-masthead__mark" href="/">
            {/* Two rings and a centre: a dial, small enough to read as a
                mark rather than as an illustration of one. */}
            <span className="es-masthead__dot" aria-hidden="true" />
            MorganHacks
          </a>
          <nav className="es-nav" aria-label="Pages">
            {NAV.map((item) => (
              <a
                key={item.id}
                href={item.href}
                aria-current={item.id === current ? "page" : undefined}
              >
                {item.label}
              </a>
            ))}
          </nav>
        </div>
      </header>

      <div className="es-shell">
        <main id="main">{children}</main>

        <footer className="es-footer">
          <div>
            <div className="es-footer__links">
              <a href={`mailto:${siteConfig.contactEmail}`}>{siteConfig.contactEmail}</a>
              <a href={siteConfig.socials.instagram}>Instagram</a>
              <a href={siteConfig.socials.linkedin}>LinkedIn</a>
            </div>
            <p className="es-footer__fine">
              MorganHacks is organized by students at Morgan State University in Baltimore. It is
              free to attend.
            </p>
          </div>

          {/* Hotlinked from MLH's own host, which is what they ask for. */}
          <a className="es-mlh" href={siteConfig.mlh.href}>
            <img
              src={siteConfig.mlh.badge}
              alt={`Major League Hacking ${siteConfig.mlh.season} Hackathon Season`}
            />
          </a>
        </footer>
      </div>
    </div>
  );
}
