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

      <div className="es-shell">
        <header className="es-masthead">
          <a className="es-masthead__mark" href="/">
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
        </header>

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
