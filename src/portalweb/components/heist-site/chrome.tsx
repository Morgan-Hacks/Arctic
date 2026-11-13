import { siteConfig } from "@/site.config";
import { MarkerLoop } from "../heist/marker";

const { archive, contactEmail, socials, mlh, year } = siteConfig;

/** Every page in the public site, in the order the nav lists them. */
export const NAV = [
  { href: "/", label: "Home", id: "home" },
  { href: "/schedule", label: "Schedule", id: "schedule" },
  { href: "/sponsors", label: "Sponsors", id: "sponsors" },
  { href: "/archive", label: "Five years", id: "archive" },
] as const;

export type NavId = (typeof NAV)[number]["id"];

/**
 * The MLH trust badge.
 *
 * MLH's terms put this in a top corner of the page and require it served from
 * their host rather than vendored, which is why it is a plain <img> with an
 * absolute src -- next/image would mean a remote pattern in next.config.ts for
 * one decorative SVG that must not be optimised anyway.
 *
 * Fixed rather than placed in the nav: the badge is 80x139, and nothing that
 * shape fits in a row 60px tall without being shrunk past legibility. The nav
 * reserves room for it on the right so the button never slides underneath.
 */
export function MlhBadge() {
  return (
    <a
      className="heist-mlh"
      href={mlh.href}
      target="_blank"
      rel="noopener noreferrer"
    >
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={mlh.badge}
        alt={`Major League Hacking ${mlh.season} Hackathon Season`}
        width={80}
        height={139}
      />
    </a>
  );
}

/**
 * The wordmark, set the way the portal sets it.
 *
 * MORGAN in chalk, HACKS in orange, the two-digit year in a mono tag. It is
 * the one piece of the portal's header worth repeating here, because somebody
 * who signs in should recognise the thing they just left.
 */
export function Wordmark({ as = "a", href = "/" }: { as?: "a" | "span"; href?: string }) {
  const inner = (
    <>
      <span>
        MORGAN<b>HACKS</b>
      </span>
      <i>{String(year).slice(-2)}</i>
    </>
  );
  if (as === "span") return <span className="heist-wordmark">{inner}</span>;
  return (
    <a className="heist-wordmark" href={href}>
      {inner}
    </a>
  );
}

/**
 * The site header.
 *
 * Plain anchors rather than next/link throughout the public site. These pages
 * are static and tiny, the whole site is four of them, and a client router on
 * a page with no state to preserve buys nothing while shipping the runtime to
 * everybody who reads one sentence and leaves.
 */
export function HeistNav({ current }: { current: NavId }) {
  return (
    <header className="heist-nav">
      <div className="heist-nav__row">
        <Wordmark />
        <nav className="heist-nav__links" aria-label="MorganHacks">
          {NAV.map((item) => (
            <a
              key={item.id}
              href={item.href}
              aria-current={item.id === current ? "page" : undefined}
            >
              {item.label}
              {/* The hand-drawn loop around the current tab, redrawn on each
                  page load. Same component the portal circles its tabs with. */}
              {item.id === current ? <MarkerLoop id={item.id} /> : null}
            </a>
          ))}
        </nav>
        <a className="heist-button heist-button--primary heist-nav__cta" href="/sponsors">
          Sponsor us
        </a>
      </div>
    </header>
  );
}

/** The scrolling strip under the header. Decorative, and marked as such. */
export function HeistTicker() {
  const line = `■ MorganHacks ${year}     Fifth edition     Morgan State University     One weekend. One job.     The crew is the key     `;
  return (
    <div className="heist-ticker" aria-hidden="true">
      <div>
        {[0, 1].map((i) => (
          <span key={i}>{line}</span>
        ))}
      </div>
    </div>
  );
}

export function HeistFoot() {
  return (
    <footer className="heist-foot">
      <div className="heist-shell">
        <div className="heist-foot__top">
          <div className="heist-foot__about">
            <Wordmark as="span" />
            <p>
              A student hackathon at Morgan State University, run by students,
              free to attend. Now in its fifth year.
            </p>
          </div>

          <div className="heist-foot__col">
            <h2>This year</h2>
            <ul>
              {NAV.filter((n) => n.id !== "home").map((item) => (
                <li key={item.id}>
                  <a href={item.href}>{item.label}</a>
                </li>
              ))}
              <li>
                <a href={`mailto:${contactEmail}`}>Contact us</a>
              </li>
            </ul>
          </div>

          <div className="heist-foot__col">
            <h2>Past years</h2>
            <ul>
              {archive.map((edition) => (
                <li key={edition.year}>
                  <a href={edition.href} target="_blank" rel="noopener noreferrer">
                    MorganHacks {edition.year}
                  </a>
                </li>
              ))}
            </ul>
          </div>

          <div className="heist-foot__col">
            <h2>Elsewhere</h2>
            <ul>
              <li>
                <a href={socials.instagram} target="_blank" rel="noopener noreferrer">
                  Instagram
                </a>
              </li>
              <li>
                <a href={socials.linkedin} target="_blank" rel="noopener noreferrer">
                  LinkedIn
                </a>
              </li>
              <li>
                <a href={socials.tiktok} target="_blank" rel="noopener noreferrer">
                  TikTok
                </a>
              </li>
            </ul>
          </div>
        </div>

        <div className="heist-foot__bottom">
          <span>MorganHacks {year}, Morgan State University, Baltimore</span>
          <a href={`mailto:${contactEmail}`}>{contactEmail}</a>
        </div>
      </div>
    </footer>
  );
}
