import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { DARK_MODE, HACKER_PORTAL, READING_MODE, isOn } from "@/lib/features";
import { READING_MODE_COOKIE, initialReadingMode } from "../../../../libs/ui/reading-mode";
import { THEME_COOKIE, parseTheme } from "../../../../libs/ui/theme";
import { cookies } from "next/headers";
import { ReadingToggle } from "../../components/reading-toggle";
import { ThemeToggle } from "../../components/theme-toggle";
import Image from "next/image";
import logo from "@/public/brands/morganhacks.png";
import { siteConfig } from "@/site.config";
import "../../../../libs/ui/reading-mode.css";
import "./portal.css";

export const metadata: Metadata = {
  title: `MorganHacks ${siteConfig.year} — your application`,
  // Nothing here should ever be indexed or previewed. It is behind a sign-in
  // and everything past it is somebody's personal data. This overrides the
  // public site's metadata for every route under /portal.
  robots: { index: false, follow: false },
};

/**
 * The frame every portal screen sits in.
 *
 * Deliberately thin: a wordmark, and a footer with the one address a person
 * can write to when the portal cannot help them. The tab row belongs to the
 * signed-in group below this, because showing "Profile" and "Messages" to
 * somebody who is not signed in only offers them two more ways to be told to
 * sign in.
 */
export default async function PortalLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  // Every screen under /portal passes through here, including the sign-in page,
  // so this is the one place the door has to be shut. Redirecting rather than
  // rendering a notice: there is nothing useful to say to somebody holding a
  // link to a portal that is closed, and the page they actually want is the
  // public one.
  //
  // On the server, before anything is sent. A check inside a client component
  // would ship the portal's markup and then navigate away from it, which is a
  // flash of a page somebody was not meant to see.
  if (!isOn(HACKER_PORTAL)) {
    redirect("/");
  }

  // Reading mode, decided on the server so the very first byte already carries
  // it. `undefined` means the cookie was absent and the client's OS preference
  // decides after mount — see libs/ui/reading-mode.ts for why those two cases
  // are deliberately different.
  //
  // Read here rather than in the root layout: that layout also wraps the
  // public page, and a cookies() call in it would cost `/` its prerender the
  // moment the flag is on. The attribute therefore goes on the portal frame
  // rather than <html> — every rule in libs/ui/reading-mode.css keys off any
  // ancestor carrying `[data-reading="on"]`, and this frame is the ancestor of
  // everything the mode applies to.
  const reading = isOn(READING_MODE) ? initialReadingMode((await cookies()).get(READING_MODE_COOKIE)?.value) : undefined;

  // The theme choice, for the control's three positions only. The palette
  // itself was decided in app/layout.tsx and painted on the first byte, since
  // every dark rule in tokens.css is written against `:root` and only the root
  // layout renders <html>. `undefined` is drawn as "Auto".
  const theme = isOn(DARK_MODE) ? parseTheme((await cookies()).get(THEME_COOKIE)?.value) : undefined;

  return (
    <div className="portal" data-reading={reading ? "on" : undefined}>
      <header className="portal__bar">
        {/*
          A plain anchor rather than next/link. It leaves the portal for the
          public site, which is the other route group and the other stylesheet
          — a client-side navigation would carry this one along with it.
        */}
        <a className="portal__brand" href="/">
          {/*
            The mark rather than the words. The console shows it on every
            screen an organizer sees, and an applicant arriving from an email
            should land on something they recognise as the same event rather
            than on a wordmark typed out in the page's own font.

            priority, because this is above the fold on every screen in the
            portal and a logo that fades in after the text has settled reads
            as a page still loading.
          */}
          <Image
            src={logo}
            alt="MorganHacks"
            className="portal__mark"
            height={28}
            priority
          />
          <span>portal</span>
        </a>

        {/*
          Reading mode, behind enable_reading_mode_feature.

          Rendered only when the flag is on, so with the flag off there is no
          control anywhere in the portal and nothing on the page mentions it —
          a feature that is off is meant to be indistinguishable from one that
          was never built. See docs/feature-flags.md.

          The initial value comes from the cookie this layout already read for
          the frame's `data-reading` attribute, so the button's own state is
          correct on arrival rather than flipping a moment after hydration.
        */}
        {isOn(READING_MODE) && (
          <ReadingToggle initial={reading} />
        )}

        {/*
          The theme control, behind enable_dark_mode_feature, and off means no
          control anywhere in the portal — the same bar reading mode is held to.

          Beside the reading toggle because they answer the same kind of
          question: how this page looks to the person reading it, rather than
          anything it can do.
        */}
        {isOn(DARK_MODE) && (
          <ThemeToggle initial={theme} />
        )}
      </header>

      {children}

      <footer className="portal__foot">
        Something wrong, or a question this page does not answer?{" "}
        <a href={`mailto:${siteConfig.contactEmail}`}>
          {siteConfig.contactEmail}
        </a>
      </footer>
    </div>
  );
}
