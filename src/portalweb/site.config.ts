/**
 * The one file next year's team edits.
 *
 * Every fact the page states lives here. Nothing in `app/` hardcodes a date,
 * an address or a URL. Figures come from the 2027 recruitment deck — if they
 * change there, change them here.
 */
export const siteConfig = {
  url: "https://morganhacks.com",

  /** The edition being recruited for. */
  year: "2027",

  /**
   * Whether the organizer drive is running.
   *
   * `false` swaps the page for a holding message: no "Apply now", no
   * countdown, and nothing that implies a form is waiting. The recruitment
   * copy stays in the file beside it, so bringing the drive back is this one
   * word rather than a rebuild.
   *
   * It is a switch rather than a date comparison on `deadline` on purpose. A
   * drive can be paused, extended or reopened for reasons no clock knows
   * about, and a page that decides on its own is a page nobody can correct in
   * a hurry.
   *
   * Turned off on 2026-11-08: the deadline below passed on 13 September and
   * the page had gone on offering "Apply now" next to "Applications are
   * closed" for eight weeks, with the button still pointing at a live form.
   */
  recruiting: false,

  /**
   * What the page says while `recruiting` is false.
   *
   * Deliberately promises nothing it cannot keep. The year is safe -- it is on
   * the MLH badge already -- and everything past that is the one honest thing
   * there is to say until somebody publishes a date.
   */
  holding: {
    headline: "Something is being",
    /** Set in italic, the way the recruitment headline emphasises its last beat. */
    headlineEmphasis: "planned",
    line: "Dates, applications and the rest: to be announced.",
    follow: "Follow along and you will hear first.",
  },

  /** The organizer application form. Verified live 2026-08-30. */
  organizerFormUrl: "https://forms.gle/SbJBfwhE3sPAARE66",

  /**
   * Applications close 2026-09-13 at 11:59 PM EST. Verified as a Sunday.
   * After that date this page needs new copy — it is not self-expiring.
   */
  deadline: {
    date: "September 13",
    weekday: "Sunday",
    /** Label as published on the recruitment flyer. */
    time: "11:59 PM EST",
    /**
     * The same instant, for the countdown. Note September 13 falls inside
     * daylight saving, so the real offset is EDT (UTC-4) even though the
     * flyer says EST. 23:59 EDT = 03:59Z the next day.
     */
    iso: "2026-09-14T03:59:00Z",
  },

  /**
   * When the event starts, for the portal's countdown.
   *
   * PLACEHOLDER: the asset kit says "April 2027" and nothing more specific has
   * been published. Replace with the real opening time before the Heist theme
   * ships; set it to null and the countdown does not render.
   */
  event: {
    startIso: "2027-04-09T22:00:00Z" as string | null,
  },

  /**
   * The address an applicant is told to write to.
   *
   * info@ rather than the gmail that was here: it is routed by Cloudflare on
   * the apex, so a reply reaches somebody, and a personal gmail address in the
   * footer of the page people read when something has gone wrong is the wrong
   * thing to be showing them.
   */
  contactEmail: "info@morganhacks.com",

  /**
   * MLH trust badge. Hotlinked from MLH's own S3, same as the 2026 site did —
   * MLH expect the badge served from their host, not vendored.
   * Season tracks the event, so this is the 2027 badge, not 2026's.
   */
  mlh: {
    season: "2027",
    badge:
      "https://s3.amazonaws.com/logged-assets/trust-badge/2027/mlh-trust-badge-2027-white.svg",
    href: "https://mlh.io/na?utm_source=na-hackathon&utm_medium=TrustBadge&utm_campaign=2027-season&utm_content=white",
  },

  socials: {
    instagram: "https://www.instagram.com/morgan.hacks",
    linkedin: "https://www.linkedin.com/company/morganhacks",
    tiktok: "https://www.tiktok.com/@morganhacks2026",
  },
} as const;
