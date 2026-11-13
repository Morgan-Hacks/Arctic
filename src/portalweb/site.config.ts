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
   * The fifth edition, and the only place its framing is written down.
   *
   * 2023, 2024, 2025 and 2026 happened; 2027 is the fifth. That arithmetic is
   * the whole of the anniversary claim, and it is worth stating here rather
   * than in the page so that next year's team changes one number instead of
   * hunting for the word "fifth" in four files.
   */
  edition: {
    ordinal: "Fifth",
    eyebrow: "Fifth edition, April 2027",

    /**
     * The headline, in the two lines it is set in.
     *
     * Two strings because the page animates each line up from behind its own
     * mask and needs them as separate elements. A single string with a marker
     * in it would mean the page parsing its own copy to find the break.
     *
     * Keep both lines short. The mask is per line, so a line long enough to
     * wrap inside its own mask arrives as two lines sliding up together and
     * the effect, along with the composition, falls apart. At the size this is
     * set, roughly twenty characters is the ceiling.
     */
    headlineTop: "Five years in.",
    headlineBottom: "Still building.",

    lede:
      "MorganHacks is a student hackathon at Morgan State University in Baltimore. You arrive on Saturday morning with an idea or none at all, and you leave on Sunday afternoon having built something with people you mostly had not met.",

    /**
     * What a search result and a shared link say once this site is the site.
     *
     * Separate from `announcement.description` below, which belongs to the
     * holding page, because a description outlives the page it describes: it
     * sits in an index until a recrawl and it renders in the preview of every
     * link already pasted into a group chat.
     */
    description:
      "MorganHacks returns to Morgan State University in April 2027 for its fifth edition. Twenty-four hours, free to attend, open to students.",
  },

  /**
   * The three questions somebody has before they read anything else.
   *
   * Rendered as a labelled list rather than folded into a paragraph, because
   * that is the shape people scan. "Dates to come" is the honest answer: the
   * asset kit says April 2027 and nothing more specific has been published.
   */
  facts: [
    { label: "Where", value: "Morgan State University, Baltimore" },
    { label: "When", value: "April 2027, dates to come" },
    { label: "Who", value: "Students, all levels, no experience needed" },
    { label: "Cost", value: "Free, including food" },
  ],

  /**
   * What the weekend actually is, for somebody who has never been to one.
   *
   * Here rather than in the page because it is copy, and copy that lives in
   * JSX is copy nobody who writes copy can find.
   */
  howItWorks: [
    {
      title: "Bring an idea, or borrow one",
      body: "Sponsors set challenges and we publish a few themes at the opening session. Plenty of people turn up with nothing decided and that is a normal way to start.",
    },
    {
      title: "Find three other people",
      body: "Teams are up to four. Come with yours or build one in the first hour. Most people arrive on their own.",
    },
    {
      title: "Build it, then show it",
      body: "You get roughly twenty-four hours. Mentors are in the room the whole time. At the end you demo it at your table to a judge who asks real questions.",
    },
  ],

  /**
   * Every edition, including the one being recruited for.
   *
   * Dates appear only where that edition's own site still states them. 2023
   * says April 1-2, 2024 says April 6-7 and 2026 says April 11-12, each read
   * off the live page rather than remembered. 2025's site publishes no date
   * anywhere, so it carries none here: an archive that invents a date for a
   * hackathon that already happened is worse than one that admits it does not
   * know, because there is nobody left who would catch it.
   *
   * `href` is null for the current edition, which is this site. All four past
   * sites answered 200 when this was written; if one stops, the card is still
   * true and the dead link is a thing to notice rather than to hide.
   */
  editions: [
    {
      year: "2023",
      dates: "April 1-2, 2023",
      note: "The first one.",
      href: "https://2023.morganhacks.com" as string | null,
    },
    {
      year: "2024",
      dates: "April 6-7, 2024",
      note: "The second, same weekend in April.",
      href: "https://2024.morganhacks.com" as string | null,
    },
    {
      year: "2025",
      dates: null as string | null,
      note: "The third. Its site never published the dates.",
      href: "https://2025.morganhacks.com" as string | null,
    },
    {
      year: "2026",
      dates: "April 11-12, 2026",
      note: "The fourth, and the last before this rebuild.",
      href: "https://2026.morganhacks.com" as string | null,
    },
    {
      year: "2027",
      dates: null as string | null,
      note: "The fifth. You are reading it.",
      href: null as string | null,
    },
  ],

  /**
   * The run of show, and the word that has to stay on it.
   *
   * PROVISIONAL. Nothing here has been agreed with the venue, and the
   * `provisional` flag is what the schedule page renders its banner from. Do
   * not take the flag off until the times are real: the shape of a hackathon
   * weekend is predictable, the hours are not, and a schedule that looks
   * settled is one people book travel around.
   *
   * The shape is the useful part, and it is worth publishing now because it
   * answers the question every first-timer asks, which is what on earth
   * happens for twenty-four hours.
   *
   * `kind` drives nothing but the dot: `deadline` is the only one that has to
   * stand out, because it is the only item with a consequence for missing it.
   */
  schedule: {
    provisional: true,
    note: "Times below are provisional and will move. The shape of the weekend will not.",
    days: [
      {
        label: "Saturday",
        date: "April 2027",
        items: [
          { time: "08:00", title: "Doors and check-in", detail: "Badges, breakfast, and somewhere to leave your bag.", kind: "open" },
          { time: "10:00", title: "Opening session", detail: "What you are building, what the prizes are, and how judging works.", kind: "talk" },
          { time: "11:00", title: "Team forming", detail: "A structured hour for anyone who turned up without a team.", kind: "build" },
          { time: "12:30", title: "Lunch", detail: null, kind: "meal" },
          { time: "14:00", title: "Beginner workshops", detail: "Short practical sessions. Git, APIs, and getting something on a screen.", kind: "talk" },
          { time: "18:30", title: "Dinner", detail: null, kind: "meal" },
          { time: "22:00", title: "Late session", detail: "Something to stand up for at the point everyone needs it.", kind: "build" },
        ],
      },
      {
        label: "Sunday",
        date: "April 2027",
        items: [
          { time: "01:00", title: "Overnight", detail: "The room stays open and there is quiet space for anyone who wants to sleep.", kind: "build" },
          { time: "08:00", title: "Breakfast", detail: null, kind: "meal" },
          { time: "11:00", title: "Submissions close", detail: "A hard deadline. Submit early and keep editing until it locks.", kind: "deadline" },
          { time: "12:00", title: "Judging", detail: "Judges come to your table. Four minutes to demo, then questions.", kind: "talk" },
          { time: "15:00", title: "Closing and prizes", detail: "Results, photographs, and everybody goes home to sleep.", kind: "talk" },
        ],
      },
    ],
  },

  /**
   * What a sponsor is actually being offered.
   *
   * There are deliberately no sponsor names or logos in this file. None are
   * confirmed for 2027, and a logo wall filled in from last year's partners
   * claims a relationship nobody has agreed to this year, which is a legal
   * problem wearing a design problem's clothes. The page shows the tiers,
   * which are ours to state, and an honest empty state where the wall goes.
   * Add names here once somebody has signed, and the wall renders itself.
   *
   * Tier names are plainly what they are. Amounts are the asking price rather
   * than a promise of what is included; the conversation after the email is
   * where that gets settled.
   */
  sponsors: {
    confirmed: [] as { name: string; href: string }[],
    pitch: {
      heading: "What sponsorship pays for",
      line: "MorganHacks runs entirely on sponsorship. It covers the room, the food, the hardware and the prizes, and it is the only reason the weekend costs a student nothing to attend.",
    },
    tiers: [
      {
        name: "Supporter",
        amount: "$500",
        benefits: [
          "Your logo on the site and the event shirt",
          "Your open roles shared with everyone in the room",
          "A thank you from the stage at the opening session",
        ],
      },
      {
        name: "Partner",
        amount: "$1,500",
        benefits: [
          "Everything in Supporter",
          "A table in the main room for the whole weekend",
          "Send mentors to sit with teams while they build",
          "Resume book from hackers who opt in",
        ],
      },
      {
        name: "Track sponsor",
        amount: "$3,500",
        benefits: [
          "Everything in Partner",
          "Set your own prize challenge and judge it",
          "A forty-five minute workshop slot on Saturday",
          "Your name on the opening slide",
        ],
      },
      {
        name: "Lead sponsor",
        amount: "$7,500",
        benefits: [
          "Everything in Track sponsor",
          "Top billing on the site, the shirt and the banner",
          "A speaking slot at the opening session",
          "First look at the projects before judging starts",
        ],
      },
    ],
  },

} as const;
