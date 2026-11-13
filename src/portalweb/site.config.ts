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
   * What the page says. All of it.
   *
   * There is no organizer drive running and no date published, so the page has
   * one fact and says it plainly rather than dressing it up. Four short strings
   * is the whole of the content; anything longer would be filling space.
   *
   * It promises nothing it cannot keep. 2027 is already public on the MLH
   * badge, Morgan State is where it has always been, and everything else is to
   * be announced -- because inventing a date here is how a placeholder becomes
   * a correction.
   *
   * Starting the next drive means writing it again rather than flipping a
   * switch. The copy, the form and the dates will all be different, and a page
   * that keeps a dead campaign on standby is how this one came to be serving a
   * closed Google Form for eight weeks after its deadline.
   */
  announcement: {
    eyebrow: "MorganHacks 2027",
    headline: "To be announced.",
    line: "Dates and applications for the next hackathon at Morgan State University.",

    /**
     * What a search result and a shared link say.
     *
     * This outlives the page: a description sits in Google's index until a
     * recrawl, and it renders in the preview of every link already pasted into
     * a group chat. The sentence that was here said applications "are open"
     * and named a deadline two months gone.
     */
    description:
      "MorganHacks at Morgan State University. Dates and applications for the next hackathon are to be announced.",
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
  /**
   * The fifth edition, and why the site says so.
   *
   * 2027 is the fifth MorganHacks: 2023, 2024, 2025, 2026, 2027. That is a
   * fact about a count, not a claim about dates, which is why it is the one
   * piece of celebration copy on the page -- see `archive` below for the four
   * editions it is counting, each of which is a site that is still up.
   */
  anniversary: {
    edition: 5,
    eyebrow: "Fifth edition",
    heading: "Five years of building at Morgan State.",
    line: "Four hackathons behind us and the fifth in April. Every one of them is still online.",

    /**
     * The headline, in the two lines it is set in.
     *
     * Split because the page sets the second line in orange underneath the
     * first, and a single string with a marker in it would mean the page
     * parsing its own copy. Two strings is the whole of that problem.
     */
    headlineTop: "Five years.",
    headlineBottom: "One more job.",

    /**
     * What a search result and a shared link say, once the Heist site is the
     * site.
     *
     * Separate from `announcement.description` above, which is the holding
     * page's, and for the same reason that one exists: a description outlives
     * the page. It sits in Google's index until a recrawl and it renders in
     * the preview of every link already pasted into a group chat. While this
     * was missing the whole Heist site was being shared with a card that said
     * the next hackathon was to be announced.
     */
    description:
      "MorganHacks returns to Morgan State University in April 2027 for its fifth year. One weekend, 24 hours, and a room full of crews.",
  },

  /**
   * The previous editions, each still served on its own subdomain.
   *
   * Dates are only here where the edition's own site still states them -- 2023
   * says April 1-2, 2024 says April 6-7 and 2026 says April 11-12, all checked
   * against the live pages rather than remembered. 2025's site publishes no
   * date anywhere, so it carries none here: an archive that invents a date for
   * a hackathon that already happened is worse than one that admits it does
   * not know, because nobody is left who would catch it.
   *
   * `live` is what the card links to. All four answered 200 when this was
   * written; if one stops, the card is still true, it is just a dead link, and
   * that is a thing to notice rather than to hide behind a feature flag.
   */
  archive: [
    {
      year: "2023",
      dates: "April 1-2, 2023",
      note: "The first one.",
      href: "https://2023.morganhacks.com",
    },
    {
      year: "2024",
      dates: "April 6-7, 2024",
      note: "The second running, same weekend in April.",
      href: "https://2024.morganhacks.com",
    },
    {
      year: "2025",
      dates: null as string | null,
      note: "The third. Its site never published the dates.",
      href: "https://2025.morganhacks.com",
    },
    {
      year: "2026",
      dates: "April 11-12, 2026",
      note: "The fourth, and the last one before this rebuild.",
      href: "https://2026.morganhacks.com",
    },
  ],

  /**
   * The run of show, and the word that has to stay on it.
   *
   * PROVISIONAL. Nothing in here has been published or agreed with the venue,
   * and the `provisional` flag below is what the schedule page renders its
   * banner from. Do not take the flag off until the times are real: the shape
   * of a hackathon weekend is predictable, the hours are not, and a schedule
   * that looks settled is one people plan travel around.
   *
   * The structure is the honest part -- doors, opening, forming teams, the
   * overnight stretch, submissions, judging, closing -- and it is worth
   * showing now because it answers the question every first-time hacker asks,
   * which is what actually happens for 24 hours.
   */
  schedule: {
    provisional: true,
    note: "Times are provisional and will change. The shape of the weekend will not.",
    days: [
      {
        label: "Saturday",
        date: "April 2027",
        items: [
          { time: "08:00", title: "Doors and check-in", detail: "Badges, breakfast, and somewhere to put your bag.", kind: "open" },
          { time: "10:00", title: "Opening ceremony", detail: "The brief: what you are building and what you are judged on.", kind: "main" },
          { time: "11:00", title: "Team forming", detail: "Come with a crew or leave with one. Nobody hacks alone unless they want to.", kind: "main" },
          { time: "12:30", title: "Lunch", detail: null, kind: "meal" },
          { time: "14:00", title: "Workshops", detail: "Short, practical sessions for people who have not done this before.", kind: "side" },
          { time: "18:30", title: "Dinner", detail: null, kind: "meal" },
          { time: "22:00", title: "Midnight run", detail: "The late event. Caffeine, something silly, and a reason to stand up.", kind: "side" },
        ],
      },
      {
        label: "Sunday",
        date: "April 2027",
        items: [
          { time: "01:00", title: "The long stretch", detail: "The room stays open. Quiet space for anyone who wants to sleep.", kind: "side" },
          { time: "08:00", title: "Breakfast", detail: null, kind: "meal" },
          { time: "11:00", title: "Submissions close", detail: "Hard deadline. Submit early and keep working; you can edit until it locks.", kind: "stop" },
          { time: "12:00", title: "Judging", detail: "Judges come to your table. Four minutes, then questions.", kind: "main" },
          { time: "15:00", title: "Closing and prizes", detail: "Winners, photos, and everyone goes home.", kind: "main" },
        ],
      },
    ],
  },

  /**
   * What a sponsor is actually being offered.
   *
   * There are deliberately no sponsor names or logos anywhere in this file.
   * None have been confirmed for 2027, and a logo wall filled in from last
   * year's partners claims a relationship nobody has agreed to this year --
   * which is a real problem rather than a design one. The page shows the
   * tiers, which are ours to state, and an honest empty state where the wall
   * goes. Add names here only once somebody has signed.
   *
   * Amounts are the asking price, not a promise of what is included; the
   * conversation that follows the email is where that is settled.
   */
  sponsors: {
    confirmed: [] as { name: string; href: string }[],
    pitch: {
      heading: "Back the crew.",
      line: "MorganHacks runs on sponsorship. It pays for the room, the food, the hardware and the prizes, and it is why the weekend costs a student nothing to attend.",
    },
    /** What a sponsor is told they get, smallest first. */
    tiers: [
      {
        name: "Accomplice",
        amount: "$500",
        benefits: ["Logo on the site and the event shirt", "Your roles shared in the hacker Slack", "A thank you from the stage"],
      },
      {
        name: "Getaway driver",
        amount: "$1,500",
        benefits: ["Everything above", "A table in the main room for the weekend", "Send mentors to help teams build", "Resume book from opted-in hackers"],
      },
      {
        name: "Safecracker",
        amount: "$3,500",
        benefits: ["Everything above", "Sponsor a prize track and judge it", "Run a 45-minute workshop", "Your name on the opening slide"],
      },
      {
        name: "Mastermind",
        amount: "$7,500",
        benefits: ["Everything above", "Title billing on the site, shirt and banner", "Speaking slot at the opening ceremony", "First look at the projects before judging"],
      },
    ],
  },

} as const;
