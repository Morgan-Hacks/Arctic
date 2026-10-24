import type { Metadata } from "next";
import { Inter, Instrument_Serif } from "next/font/google";
import localFont from "next/font/local";
import { cookies } from "next/headers";
import { DARK_MODE, isOn } from "@/lib/features";
import { THEME_COOKIE, initialTheme } from "../../../libs/ui/theme";
import { siteConfig } from "@/site.config";

// No stylesheet here on purpose. This app serves two things with nothing in
// common but a hostname — the public page and the hacker portal — and each
// route group brings its own. globals.css locks the body to one screen, which
// is right for the recruitment page and would leave the portal unable to
// scroll past the fold.

const inter = Inter({
  subsets: ["latin"],
  display: "swap",
  variable: "--font-inter",
});

/**
 * The reading face, behind enable_reading_mode_feature.
 *
 * OpenDyslexic, from @fontsource/opendyslexic rather than committed to this
 * repository — it is SIL OFL 1.1, which permits web distribution, and taking
 * it from npm keeps a font binary out of the tree while still self-hosting it.
 * Next hashes and serves the file itself, so nothing is fetched from a CDN.
 *
 * Regular and bold only. The console leans on 600-weight emphasis and two real
 * files look better than synthetic weight between them; an italic is not
 * offered because nothing in reading mode sets any.
 *
 * `preload: false` because reading mode is off by default. Preloading a face
 * almost nobody uses costs every applicant a request and a chunk of bandwidth
 * to save a reader who has it on a fraction of a second.
 */
const openDyslexic = localFont({
  src: [
    {
      path: "../node_modules/@fontsource/opendyslexic/files/opendyslexic-latin-400-normal.woff2",
      weight: "400",
      style: "normal",
    },
    {
      path: "../node_modules/@fontsource/opendyslexic/files/opendyslexic-latin-700-normal.woff2",
      weight: "700",
      style: "normal",
    },
  ],
  display: "swap",
  preload: false,
  variable: "--font-opendyslexic",
  // The family name comes from the font file, so this is the name the CSS in
  // libs/ui/reading-mode.css refers to. next/font/local cannot rename the
  // family, and renaming it is what the OFL forbids without a new name anyway.
  fallback: ["sans-serif"],
});

/** Display face. Carries the whole page, so it is the only other font loaded. */
const display = Instrument_Serif({
  subsets: ["latin"],
  weight: "400",
  style: ["normal", "italic"],
  display: "swap",
  variable: "--font-display",
});

const title = `MorganHacks ${siteConfig.year} — organizer applications`;
const description = `Organizer applications for MorganHacks ${siteConfig.year} are open to all college students. Applications close ${siteConfig.deadline.weekday}, ${siteConfig.deadline.date} at ${siteConfig.deadline.time}.`;

export const metadata: Metadata = {
  metadataBase: new URL(siteConfig.url),
  title,
  description,
  openGraph: {
    title,
    description,
    url: siteConfig.url,
    siteName: "MorganHacks",
    locale: "en_US",
    type: "website",
    // The image itself comes from app/opengraph-image.tsx, which Next wires up
    // automatically — listing it here as well would only let the two drift.
  },
  twitter: { card: "summary_large_image", title, description },
};

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  // No reading-mode resolution here on purpose. This layout wraps the public
  // page as well as the portal, and calling cookies() in it would pull the
  // prerendered `/` into dynamic rendering the moment the flag is on. The
  // cookie is read in app/portal/layout.tsx instead, which applies
  // `data-reading` to the portal frame — reading-mode.css keys off any
  // ancestor carrying the attribute, so the treatment reaches everything under
  // that frame and `/` keeps its prerender and never sees the attribute.

  // The theme cannot dodge that the same way, and this is the one real cost in
  // the feature, so it is written down rather than discovered.
  //
  // Reading mode gets to live on the portal frame because reading-mode.css
  // keys off any ancestor carrying `data-reading`. The palette cannot: every
  // dark rule in tokens.css is written against `:root`, and `:root` is <html>,
  // which only this layout renders. Moving those selectors off `:root` so a
  // <div> could carry the theme would change which declarations win in all
  // three apps — a much larger change than a theme switch, and not one to make
  // by the way.
  //
  // So the cookie is read here, and only when the flag is on. With the flag off
  // — which is how this merges — cookies() is never called and `/` keeps its
  // prerender exactly as before. Turning the flag on makes `/` dynamic. That is
  // the trade, and it is worth knowing before flipping it: the public page has
  // no light and no dark of its own (it is a fixed night scene with its own
  // palette and does not import tokens.css), so it pays the render cost for a
  // feature it cannot use. Giving `portal` its own root layout would buy the
  // prerender back, and that is the follow-up if the cost ever matters.
  const theme = isOn(DARK_MODE)
    ? initialTheme((await cookies()).get(THEME_COOKIE)?.value)
    : "light";
  return (
    <html
      lang="en"
      // Both palettes live in libs/ui/tokens.css, so this chooses between them
      // rather than overriding anything. `undefined` writes no attribute at
      // all, which is how a reader who has chosen nothing gets
      // `prefers-color-scheme` to answer for them.
      //
      // This was pinned to "light" until the mark had a treatment that holds on
      // a dark ground, which was the honest blocker and is now dealt with: it
      // sits on its own plate in dark, see --brand-plate in tokens.css.
      data-theme={theme}
      className={`${inter.variable} ${display.variable} ${openDyslexic.variable}`}
    >
      <body>{children}</body>
    </html>
  );
}
