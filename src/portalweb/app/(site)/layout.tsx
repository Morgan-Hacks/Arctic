import { Bricolage_Grotesque, Manrope } from "next/font/google";
import "./globals.css";

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
 */
export default function SiteLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return <div className={`${display.variable} ${body.variable}`}>{children}</div>;
}
