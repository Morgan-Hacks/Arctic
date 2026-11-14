import type { Metadata } from "next";
import { Bricolage_Grotesque, Instrument_Serif, JetBrains_Mono, Manrope, Newsreader } from "next/font/google";
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
 * The three faces the event site is set in.
 *
 * A magazine arrangement: a display serif for headlines, a text serif for
 * reading, and one mono for the furniture -- eyebrows, folios, prices, the
 * cells of the sponsor matrix. Instrument Serif and Newsreader are both
 * serifs and do not muddy, because at the sizes each is used their contrast
 * and width are nothing alike.
 *
 * `preload: false` on all three. next/font options have to be statically
 * analysable, so a face cannot be preloaded only on the environments where
 * the flag is on: it is preloaded everywhere or nowhere. Nowhere is the right
 * side to err on, since with the flag off nothing on the page sets them and a
 * preload would be a download for a page that never uses it.
 *
 * Instrument Serif is the one face here that is not variable -- it ships a
 * single weight -- so `weight` is required rather than optional.
 */
const serif = Instrument_Serif({
  subsets: ["latin"],
  weight: "400",
  display: "swap",
  preload: false,
  variable: "--font-serif-display",
});

const text = Newsreader({
  subsets: ["latin"],
  display: "swap",
  preload: false,
  variable: "--font-text",
});

const mono = JetBrains_Mono({
  subsets: ["latin"],
  display: "swap",
  preload: false,
  variable: "--font-mono",
});

/**
 * What a search result says, which depends on which site this is.
 *
 * The holding page's description promises nothing; the event site's names the
 * month. They are separate strings in site.config.ts for the same reason this
 * is conditional: a description outlives the page it describes.
 */
export const metadata: Metadata = isOn(EVENT_SITE)
  ? { description: siteConfig.edition.description }
  : { description: siteConfig.announcement.description };

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
 * `event-site.css` is imported unconditionally above, because a CSS import is
 * static and there is no way to make one depend on a runtime flag. It is inert
 * rather than hidden: every rule in it is scoped under `.event-site`, a class
 * that never reaches the DOM while the flag is off, so with the flag off the
 * page renders exactly as it did before. The bytes ship either way, which is
 * the one honest cost of gating a stylesheet this way.
 */
export default function SiteLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const fonts = isOn(EVENT_SITE)
    ? `${serif.variable} ${text.variable} ${mono.variable}`
    : "";

  return <div className={`${display.variable} ${body.variable} ${fonts}`}>{children}</div>;
}
