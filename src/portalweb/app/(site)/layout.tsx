import { Bricolage_Grotesque, Manrope } from "next/font/google";
import { Barlow, Barlow_Condensed, Courier_Prime } from "next/font/google";
import { HEIST_THEME, isOn } from "@/lib/features";
import "./globals.css";
import "./heist.css";

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
 * The Heist kit's faces, behind enable_heist_theme_feature.
 *
 * The same three the portal loads, declared again here because a font variable
 * only reaches the subtree it is written on and the portal's frame is not an
 * ancestor of this one. next/font deduplicates the actual files, so declaring
 * Barlow in both places downloads it once.
 *
 * `preload: false` for the same reason the portal gives: with the theme off
 * nothing on the page sets them, and preloading three faces nobody renders is
 * three wasted requests on every visit to the holding page.
 */
const barlow = Barlow({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  display: "swap",
  preload: false,
  variable: "--font-barlow",
});

const barlowCondensed = Barlow_Condensed({
  subsets: ["latin"],
  weight: ["500", "600", "700"],
  display: "swap",
  preload: false,
  variable: "--font-barlow-condensed",
});

const courier = Courier_Prime({
  subsets: ["latin"],
  weight: ["400", "700"],
  display: "swap",
  preload: false,
  variable: "--font-courier",
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
 *
 * ------------------------------------------------------------- the theme ---
 *
 * With enable_heist_theme_feature on, this frame carries `heist-site` and the
 * Heist faces, and heist.css takes the page over: it lifts the one-screen lock
 * globals.css puts on <body> and repaints the ground navy. With the flag off
 * the class is absent, every rule in heist.css matches nothing, and what
 * renders is the holding page exactly as it was -- which is the bar
 * docs/feature-flags.md sets for a feature that is off.
 *
 * `isOn` is read here rather than in each page so the frame and the pages
 * inside it can never disagree about which site this is.
 */
export default function SiteLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const heist = isOn(HEIST_THEME);

  if (heist) {
    return (
      <div
        className={`heist-site ${barlow.variable} ${barlowCondensed.variable} ${courier.variable}`}
      >
        {children}
      </div>
    );
  }

  return <div className={`${display.variable} ${body.variable}`}>{children}</div>;
}
