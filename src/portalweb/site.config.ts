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
   * The front of the site, once enable_event_site_feature is on.
   *
   * Everything under here belongs to the three editorial pages and is read by
   * nothing while the flag is off. `announcement` above stays exactly as it is:
   * it is the holding page's copy, it is still what `/` renders with the flag
   * off, and the two sets of words outlive each other in different places.
   */
  edition: {
    /** Written out, because the pages set it in prose as well as in figures. */
    ordinal: "fifth",

    /** The standfirst above the headline. */
    eyebrow: "The fifth MorganHacks",

    /**
     * The headline, as one string.
     *
     * One field rather than the two the previous build needed. That version
     * slid each line up from behind its own mask, so the break had to be
     * authored and a line long enough to wrap destroyed the effect, which put
     * a roughly twenty-character ceiling on the copy. Nothing here animates
     * per line, so the headline can be as long as it needs to be and the
     * browser decides where it breaks.
     */
    headline: "Five Years",

    /** The deck. Sits directly under the headline, larger than body copy. */
    deck:
      "The fifth MorganHacks returns to Morgan State University in April 2027. Twenty-four hours, free to attend, open to any student.",

    /**
     * The opening essay, in the paragraphs it is set in.
     *
     * An array because the page sets the first one with a drop cap and the
     * rest without, and because two columns need real paragraph breaks to
     * balance against. Prose rather than a feature list: the thing a student
     * who has never been to a hackathon needs is a description of the
     * weekend, not a grid of selling points.
     */
    lede: [
      "MorganHacks is a student hackathon at Morgan State University in Baltimore. You arrive on Saturday morning with an idea or with none at all, and you leave on Sunday afternoon having built something with people you mostly had not met.",
      "It is free, it includes food, and it does not expect you to have done this before. Roughly half the room never has. The workshops on Saturday afternoon exist for exactly that reason, and the hour set aside for team forming is there so that turning up alone is a normal way to turn up.",
    ],

    /**
     * What a search result and a shared link say once this site is the site.
     *
     * Separate from `announcement.description`, which belongs to the holding
     * page, because a description outlives the page it describes: it sits in an
     * index until a recrawl and it renders in the preview of every link
     * already pasted into a group chat.
     */
    description:
      "MorganHacks returns to Morgan State University in April 2027 for its fifth edition. Twenty-four hours, free to attend, open to students.",
  },

  /**
   * The four questions somebody has before they read anything else.
   *
   * Set as a labelled list rather than folded into a paragraph, because that
   * is the shape people scan. "Dates to come" is the honest answer: the asset
   * kit says April 2027 and nothing more specific has been published. Put the
   * real dates here and the sentence stops apologising on its own.
   */
  facts: [
    { label: "Where", value: "Morgan State University, Baltimore" },
    { label: "When", value: "April 2027, dates to come" },
    { label: "Who", value: "Students, all levels, no experience needed" },
    { label: "Cost", value: "Free, including food" },
  ],

  /**
   * What the weekend is, in prose.
   *
   * This replaces both the step-by-step list and the hour-by-hour schedule the
   * previous build carried. The timetable was invented -- provisional times
   * that would all have moved -- and a page that sets its own filler in a
   * serif at reading size makes that obvious. These three passages say the
   * same useful thing without claiming a single time that is not yet decided.
   *
   * When the real running order exists, a /schedule page is worth building and
   * this stays as its introduction.
   */
  weekend: [
    {
      heading: "Saturday morning",
      body: "Doors open early. You collect a badge, eat breakfast, and find somewhere to leave your bag. The opening session says what you are building, what the prizes are and how judging works, and then there is a structured hour for anybody who arrived without a team.",
    },
    {
      heading: "Saturday into Sunday",
      body: "The rest of it is building. Beginner workshops run in the afternoon for anybody who wants them, mentors are in the room throughout, and meals happen on a schedule so that nobody has to decide between eating and finishing. The room stays open overnight and there is quiet space for anybody who wants to sleep.",
    },
    {
      heading: "Sunday afternoon",
      body: "Submissions close, and then everybody demonstrates what they made. Judging is done by people walking the room and asking you about it, which is less frightening than it sounds and is the part most people remember.",
    },
  ],

  /**
   * The five editions, oldest first.
   *
   * Each year had its own site and each one is still up, so the links go out
   * rather than reconstructing anything here. The notes are deliberately plain
   * and include what is missing: 2025 never published its dates, and saying so
   * is better than a blank or an invented weekend.
   *
   * 2027 is in the list with no link, because it is the page you are on.
   */
  editions: [
    {
      year: "2023",
      dates: "April 1-2, 2023" as string | null,
      note: "The first one.",
      href: "https://2023.morganhacks.com" as string | null,
    },
    {
      year: "2024",
      dates: "April 6-7, 2024" as string | null,
      note: "The second, the same weekend in April.",
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
      dates: "April 11-12, 2026" as string | null,
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
   * The sponsor prospectus.
   *
   * Shaped as a letter followed by a tier-by-perk matrix, which is the form
   * every hackathon prospectus takes and the form a company's sponsorship
   * lead expects to read. They are comparing you against four other events
   * that week, and a matrix is what makes that comparison possible in under a
   * minute. Prose alone makes them do the work.
   */
  sponsors: {
    /**
     * Sponsors who have signed, as they should appear.
     *
     * Empty, and the page renders nothing at all rather than an empty row of
     * placeholder tiles. A grid captioned "our sponsors" with nothing in it
     * argues the opposite of what it is there to argue.
     */
    confirmed: [] as { name: string; href: string }[],

    /**
     * The opening letter, addressed to the person reading it.
     *
     * Deliberately short. It says what the event is, who is in the room, and
     * what their money pays for, and then it gets out of the way of the
     * table, which is the part they came for.
     *
     * It states no attendance figure, because nobody has confirmed one. Put a
     * real number in the second paragraph the moment you have it: it is the
     * single most load-bearing fact on this page, and its absence is the
     * first thing a sponsorship lead will notice.
     */
    letter: [
      "MorganHacks is a student hackathon at Morgan State University, a historically Black university in Baltimore. April 2027 is the fifth one.",
      "It is free for everybody who attends, and it is free because it is sponsored. Sponsorship pays for the food, the space and the prizes. What it buys you is a weekend in a room with students who are building things and are about to start looking for internships.",
      "We are a student organization, not a company, and we would rather tell you what we can actually deliver than oversell a room you have not seen. If a tier below is close but not right, say so and we will talk.",
    ],

    /** Where a prospective sponsor writes. */
    contact: "info@morganhacks.com",

    /**
     * The tiers, cheapest first, in the order the matrix columns run.
     *
     * `highlight` draws the one column the page leans on. One only: a table
     * where every column is emphasised is a table where none is.
     */
    tiers: [
      { id: "supporter", name: "Supporter", price: "$500", highlight: false },
      { id: "partner", name: "Partner", price: "$1,500", highlight: false },
      { id: "track", name: "Track", price: "$3,500", highlight: true },
      { id: "lead", name: "Lead", price: "$7,500", highlight: false },
    ],

    /**
     * The matrix, as groups of rows.
     *
     * Each perk carries one cell per tier, in the same order as `tiers` above.
     * `true` renders a check, `false` renders an empty cell, and a string
     * renders as itself -- which is how a perk that scales with the tier ("10
     * minutes") says so in the cell rather than needing a row of its own.
     *
     * PLACEHOLDER, and more urgently than anything else in this file. Every
     * cell below is a promise to a company that will hold you to it, and none
     * of them has been agreed by anybody. Go through it line by line with
     * whoever runs sponsorship before this page is reachable from the
     * internet. The amounts carry over from the recruitment deck; the perks
     * are a reasonable starting shape and nothing more.
     */
    perkGroups: [
      {
        group: "At the event",
        perks: [
          {
            label: "Mentors in the room",
            help: "Your engineers spend the weekend helping teams who are stuck.",
            cells: [true, true, true, true],
          },
          {
            label: "Named at the opening",
            help: "How long you have on stage on Saturday morning.",
            cells: ["Named", "2 min", "5 min", "10 min"],
          },
          {
            label: "Put up a prize",
            help: "A prize of your own, awarded at the closing session.",
            cells: [false, true, true, true],
          },
          {
            label: "A track of your own",
            help: "A themed challenge carrying your name, which teams opt into.",
            cells: [false, false, true, true],
          },
          {
            label: "A judge on the panel",
            help: "One of your people judges alongside the organizers.",
            cells: [false, false, true, true],
          },
          {
            label: "Run a workshop",
            help: "A session on Saturday afternoon. Yours to run.",
            cells: [false, false, "30 min", "45 min"],
          },
        ],
      },
      {
        group: "Hiring",
        perks: [
          {
            label: "A table in the room",
            help: "Somewhere to stand, all weekend, where students will come to you.",
            cells: [false, true, true, true],
          },
          {
            label: "Recruiters on site",
            help: "Bring people whose job is hiring, not just engineers.",
            cells: [false, true, true, true],
          },
          {
            label: "Resumes, where shared",
            help: "Only from attendees who opted in to sharing them with sponsors. Nobody else is in the file.",
            cells: [false, false, true, true],
          },
          {
            label: "A message to attendees",
            help: "One email, written by you, sent by us before the event.",
            cells: [false, false, false, true],
          },
        ],
      },
      {
        group: "Your name on it",
        perks: [
          {
            label: "Logo on this site",
            help: "On the sponsors page and in the footer.",
            cells: [true, true, true, true],
          },
          {
            label: "Logo on the shirt",
            help: "The shirt every attendee goes home in.",
            cells: [false, true, true, true],
          },
          {
            label: "Posts from our accounts",
            help: "Instagram, LinkedIn and TikTok, in the run-up.",
            cells: [false, true, true, true],
          },
          {
            label: "Billed as cohost",
            help: "Your name next to ours everywhere the event is named.",
            cells: [false, false, false, true],
          },
        ],
      },
    ],
  },

} as const;
