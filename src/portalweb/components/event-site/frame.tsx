import { siteConfig } from "@/site.config";

const { mlh, contactEmail, socials, year } = siteConfig;

/**
 * The four pages, in the order the rail lists them.
 *
 * One array, used for the rail, the dock and the footer, so the three can
 * never disagree about what this site contains. Labels are one word each
 * because the dock on a phone has to fit all four across it; the index
 * numbers carry the structure on a wide screen, where there is room.
 */
export const NAV = [
  { id: "home", n: "01", label: "Overview", href: "/" },
  { id: "schedule", n: "02", label: "Weekend", href: "/schedule" },
  { id: "sponsors", n: "03", label: "Sponsors", href: "/sponsors" },
  { id: "editions", n: "04", label: "Editions", href: "/editions" },
] as const;

export type NavId = (typeof NAV)[number]["id"];

/**
 * The whole frame: skip link, badge, rail, content column, footer.
 *
 * A component rather than the route-group layout, because the rail has to
 * know which page is open and a layout cannot be told. Each page names itself
 * once, at the top, and gets the rest.
 *
 * Plain <a> rather than next/link throughout. This is four static pages with
 * no shared state to preserve across a navigation, and a plain anchor means
 * no prefetch waterfall and nothing for the rail to hydrate -- none of these
 * components is a client component, and that is worth keeping.
 */
export function Frame({
  current,
  children,
}: Readonly<{ current: NavId; children: React.ReactNode }>) {
  return (
    <>
      <a className="es-skip" href="#main">
        Skip to content
      </a>

      {/* MLH ask for the badge served from their host, not vendored, and
          pinned to a top corner. A plain <img> because it is an SVG, so
          next/image would mean a remote pattern in the config for nothing. */}
      <a className="es-mlh" href={mlh.href} target="_blank" rel="noopener noreferrer">
        <img
          src={mlh.badge}
          alt={`Major League Hacking ${mlh.season} Hackathon Season`}
          width={80}
          height={139}
        />
      </a>

      {/* Carries the wordmark only when the rail has turned into the bottom
          dock and given it up. Hidden above 60rem. */}
      <div className="es-topbar">
        <a href="/">
          <span className="es-mark">
            MorganHacks
            <span className="es-mark__yr">{year}</span>
          </span>
        </a>
      </div>

      <header className="es-rail">
        <a className="es-rail__brand" href="/">
          <span className="es-mark">
            MorganHacks
            <span className="es-mark__yr">{year}</span>
          </span>
        </a>

        <nav aria-label="Sections">
          <ul className="es-nav">
            {NAV.map((item) => (
              <li key={item.id}>
                <a
                  href={item.href}
                  // aria-current is what tells a screen reader which page this
                  // is, and it is also the hook the accent styling hangs off,
                  // so the two can never say different things.
                  aria-current={item.id === current ? "page" : undefined}
                >
                  <span className="es-nav__n" aria-hidden="true">
                    {item.n}
                  </span>
                  <span>{item.label}</span>
                </a>
              </li>
            ))}
          </ul>
        </nav>

        <div className="es-rail__foot">
          <a href={`mailto:${contactEmail}`}>{contactEmail}</a>
          <a href={socials.instagram} target="_blank" rel="noopener noreferrer">
            Instagram
          </a>
          <a href={socials.linkedin} target="_blank" rel="noopener noreferrer">
            LinkedIn
          </a>
          <a href={socials.tiktok} target="_blank" rel="noopener noreferrer">
            TikTok
          </a>
        </div>
      </header>

      <div className="es-main">
        <main className="es-wrap" id="main">
          {children}
        </main>

        <footer className="es-foot">
          <div className="es-foot__in">
            <p>MorganHacks, Morgan State University, Baltimore.</p>
            <span className="es-foot__sp" aria-hidden="true" />
            <nav aria-label="MorganHacks elsewhere">
              <a href={`mailto:${contactEmail}`}>{contactEmail}</a>
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
        </footer>
      </div>
    </>
  );
}

/**
 * A section heading: a rule, a mark, a kicker, a name.
 *
 * One component rather than a dozen copies of the same four elements, which is
 * also what keeps the hairline rule above every section on the site at the
 * same distance from the heading under it.
 */
export function SectionHead({
  kicker,
  heading,
  lede,
}: Readonly<{ kicker: string; heading: string; lede?: string }>) {
  return (
    <div className="es-sec__head">
      <p className="es-eyebrow">
        <span className="es-eyebrow__sq" aria-hidden="true" />
        {kicker}
      </p>
      <h2 className="es-h2">{heading}</h2>
      {lede ? <p className="es-lede">{lede}</p> : null}
    </div>
  );
}
