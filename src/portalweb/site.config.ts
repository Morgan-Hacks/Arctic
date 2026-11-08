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
   * What the page says.
   *
   * There is no organizer drive running, and the page no longer pretends
   * otherwise: the 2027 recruitment copy, its Google Form and its countdown
   * were removed in November 2026, eight weeks after the deadline they named
   * had passed.
   *
   * It promises nothing it cannot keep. 2027 is already public on the MLH
   * badge, and everything past it is "to be announced", because no date has
   * been published and inventing one here is how a placeholder becomes a
   * correction.
   *
   * Starting the next drive means writing it again rather than flipping a
   * switch. That is the right cost: the copy, the form and the dates will all
   * be different, and a page that keeps a dead campaign on standby is how this
   * one came to be serving a closed form in the first place.
   */
  announcement: {
    headline: "Something is being",
    /** Set in the accent, which is the only coloured word on the page. */
    headlineEmphasis: "planned",

    /**
     * The board.
     *
     * One row that is already known, and the rest honestly empty. The known
     * row is load-bearing: a board where every value reads "To be announced"
     * looks like a page that failed to load its own content, and one real
     * fact is what tells a reader the empty rows are pending rather than
     * broken.
     *
     * Add the dates here when there are dates. Nothing else has to change.
     */
    board: [
      { label: "Dates", value: "To be announced" },
      { label: "Applications", value: "To be announced" },
      { label: "Where", value: "Morgan State University, Baltimore" },
      { label: "Updates", value: "@morgan.hacks" },
    ],

    /**
     * What a search result and a shared link say.
     *
     * This outlives the page: a description sits in Google's index until a
     * recrawl, and it renders in the preview of every link already pasted into
     * a group chat. The sentence that was here said applications "are open"
     * and named a deadline two months gone.
     */
    description:
      "MorganHacks at Morgan State University. Dates and applications for the next edition are to be announced.",
    /** The headline on the shared image, which has no room for a second line. */
    card: "Something is being planned.",
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
