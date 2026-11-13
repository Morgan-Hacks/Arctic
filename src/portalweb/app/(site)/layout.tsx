import type { Metadata } from "next";
import { Bricolage_Grotesque, Instrument_Sans, JetBrains_Mono, Manrope } from "next/font/google";
import { EVENT_SITE, isOn } from "@/lib/features";
import { siteConfig } from "@/site.config";
import "./globals.css";
import "./event-site.css";

/**
 * A distinctive display face and a refined body face, loaded here and nowhere
 * else.
 *
 * The page was set in Inter throughout, which is the single most common
 * giveaway of a page nobody chose a typeface for -- it is the default of every
 * scaffold and it reads that way. Bricolage Grotesque has a voice at headline
 * size; Manrope is quiet enough to disappear underneath it.
 *
 * Loaded in this route group rather than the root layout, which also carries
 * the portal. Fonts declared at the root are preloaded for every route, so
 * putting these there would make /portal fetch two faces it never sets. The
 * variables land on the wrapper below and reach everything inside it.
 */
const display = Bricolage_Grotesque({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-display",
});

const body = Manrope({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-body",
});

/**
 * The event site's two extra faces, behind enable_event_site_feature.
 *
 * Bricolage carries the headlines on both the holding page and the event site,
 * so it is declared once above and used by both. These two are the event
 * site's alone: Instrument Sans for running text, JetBrains Mono for the
 * indices, times and years, where a tabular monospace is doing real work
 * rather than decorating.
 *
 * `preload: false` because with the flag off nothing on the page sets either
 * one, and preloading two faces nobody renders costs every visitor to the
 * holding page two requests to save a visitor to a page that does not exist
 * yet. next/font's options have to be statically analysable, so this cannot be
 * conditional -- which is also why Manrope above is still preloaded on staging,
 * where the event site renders and nothing sets it. One wasted request on one
 * environment. When this site replaces the holding page, the holding page and
 * Manrope go together and that cost goes with them.
 */
const instrument = Instrument_Sans({
  subsets: ["latin"],
  display: "swap",
  preload: false,
  variable: "--font-instrument",
});

const mono = JetBrains_Mono({
  subsets: ["latin"],
  weight: ["500", "600"],
  display: "swap",
  preload: false,
  variable: "--font-mono",
});

/**
 * What a search result and a shared link say for this route group.
 *
 * The root layout sets the title and description from `announcement`, which is
 * the holding page's copy: "to be announced". That is correct while the holding
 * page is what renders and wrong the moment the event site is, and a
 * description is the part of a page that outlives it -- it sits in an index
 * until a recrawl and renders in the preview of every link already pasted
 * somewhere.
 *
 * The three pages inside export their own and override this; only `/` falls
 * through to it.
 *
 * Module scope rather than `generateMetadata`, because `isOn` is synchronous
 * and resolves from the environment at load. There is nothing to await and
 * nothing that can change between requests.
 */
export const metadata: Metadata = isOn(EVENT_SITE)
  ? {
      title: `MorganHacks ${siteConfig.year}`,
      description: siteConfig.edition.description,
      openGraph: {
        title: `MorganHacks ${siteConfig.year}`,
        description: siteConfig.edition.description,
        url: siteConfig.url,
        siteName: "MorganHacks",
        locale: "en_US",
        type: "website",
      },
      twitter: {
        card: "summary_large_image",
        title: `MorganHacks ${siteConfig.year}`,
        description: siteConfig.edition.description,
      },
    }
  : {};

/**
 * The public site, and the reason it sits in a route group.
 *
 * This app serves two things that share nothing but a hostname: the public
 * page, a locked single screen, and the hacker portal, a quiet document that
 * scrolls. `globals.css` sets `overflow: hidden` and a dark background on
 * `body` itself, so while it was imported by the root layout it applied to the
 * portal too — a portal that could not scroll past the fold.
 *
 * A route group is the App Router's answer to exactly that: `(site)` is
 * stripped from the URL, so `/` is still `/`, but the stylesheet is loaded for
 * these routes and not for `/portal`.
 *
 * ------------------------------------------------------------- the site ---
 *
 * With enable_event_site_feature on, this frame carries `event-site` and the
 * two extra faces, and event-site.css takes the page over: it releases the
 * one-screen lock globals.css puts on <body> and repaints the ground. With the
 * flag off the class is absent, every rule in event-site.css matches nothing,
 * and what renders is the holding page exactly as it was.
 *
 * `isOn` is read here rather than in each page so the frame and the pages
 * inside it can never disagree about which site this is.
 */
export default function SiteLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  if (isOn(EVENT_SITE)) {
    return (
      <div className={`event-site ${display.variable} ${instrument.variable} ${mono.variable}`}>
        {children}
      </div>
    );
  }

  return <div className={`${display.variable} ${body.variable}`}>{children}</div>;
}
