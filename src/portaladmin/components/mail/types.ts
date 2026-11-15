/**
 * What a campaign is, as these screens need it.
 *
 * The shapes are the API's, not a second model: a campaign is one row in
 * notify.campaigns and the statuses are the ones its check constraint allows.
 * Nothing here computes what a campaign "really" is — a screen that disagreed
 * with the sender about whether something had gone out would be worse than no
 * screen at all.
 */

/**
 * The six states a campaign can be in.
 *
 * A union rather than a string, because every one of them changes what the
 * screen offers: only a draft can be sent, only a queued campaign can be
 * cancelled, and a sent one can do neither.
 */
export type CampaignStatus =
  | "draft"
  | "queued"
  | "sending"
  | "sent"
  | "cancelled"
  | "failed";

/** A row on the list. */
export type CampaignRow = {
  id: string;
  name: string;
  status: CampaignStatus;
  recipientCount: number;
  createdAt: string;
  /** Null until it has gone out. */
  sentAt: string | null;
  templateKey?: string | null;
  trackingEnabled?: boolean;
  audience?: {
    type: "applicationStatus" | "formRespondents" | "formAnswer" | "explicitList";
    sourceId: string | null;
    statuses: string[] | null;
    count: number | null;

    /**
     * The question and the answer an answer segment was built on.
     *
     * Read off the stored criterion rather than resolved against the form, so
     * the row still says who a sent campaign was aimed at after the question
     * has been reworded or the form taken down.
     */
    question?: string | null;
    answer?: string | null;
  } | null;
  summary?: {
    sentEmails: number;
    deliveredEmails: number;
    openTrackedEmails: number;
    openedEmails: number;
    trackedEmails: number;
    clickedEmails: number;
    previewHtml: string | null;
  } | null;
};

export type CampaignListFilters = {
  search: string;
  status: CampaignStatus | "all";
  sort: "newest" | "oldest" | "name";
  page: number;
};

export type CampaignListPage = {
  items: CampaignRow[];
  total: number;
  counts: Partial<Record<CampaignStatus, number>>;
  page: number;
  pageSize: number;
};

/**
 * Who a campaign goes to.
 *
 * Four shapes and no more. Comms needs "everybody we accepted", "everybody who
 * filled in the mentor form", "everybody who said they want a hardware track"
 * and "these nine addresses" — a query builder would be a fifth thing to get
 * wrong on the one screen where being wrong means several hundred people got an
 * email meant for nine.
 *
 * `formAnswer` carries one question and one value and has no operator to
 * choose, which is what keeps it a sentence rather than the beginning of one.
 * The answer only decides who receives the email: nothing about it is copied
 * into the message, and there is no placeholder for it.
 *
 * Stored on the campaign rather than resolved and forgotten, which is what
 * makes "who exactly did we email" answerable a month later.
 */
/** An event, for the segment that has to name one. */
export type EventChoice = { id: string; name: string };

export type Segment =
  | { type: "applicationStatus"; eventId: string; statuses: string[] }
  | { type: "formRespondents"; formId: string }
  | { type: "formAnswer"; formId: string; question: string; value: string }
  | { type: "explicitList"; recipients?: ListRecipient[]; emails?: string[] };

/**
 * One person on a typed list.
 *
 * Both names are optional and usually absent: the list is as often four
 * sponsors typed in by hand as it is four hundred rows out of a spreadsheet,
 * and the first kind have no names on file anywhere. A template that greets
 * people is refused at send when somebody in the list has no name, which is
 * the check that makes leaving them optional here safe.
 */
export type ListRecipient = {
  email: string;
  firstName?: string | null;
  lastName?: string | null;
};

/**
 * The people on a typed list, whichever way the list was stored.
 *
 * `recipients` is what a list has been written as since the elements started
 * carrying names. `emails` is a bare array of strings, which is what every
 * campaign drafted before that still holds -- the rename did not rewrite the
 * documents already in the database, and a campaign's stored segment is only
 * rewritten when somebody saves that campaign again. Both shapes therefore
 * exist in the table at the same time, indefinitely.
 *
 * The API reads both; this is the same courtesy on this side. Everything that
 * reads a list goes through here, so no screen has to know which era the
 * campaign it is showing came from, and a campaign from last term cannot take
 * a page down by having the older shape.
 */
export function listRecipients(segment: {
  recipients?: ListRecipient[];
  emails?: string[];
}): ListRecipient[] {
  if (segment.recipients) return segment.recipients;
  return (segment.emails ?? []).map(email => ({ email }));
}

/**
 * One campaign, with the fields the list does not carry.
 *
 * `createdBy` and `approvedBy` are the two names on it, and they are the
 * reason this screen can say anything about the approval rule before somebody
 * presses a button: the API refuses a send whose actor is the author, and the
 * only way to know that in advance is to compare these against who is signed
 * in.
 */
export type Campaign = CampaignRow & {
  revision: number;
  updatedBy?: string | null;
  settings?: CampaignEmailSettings | null;
  templateKey?: string | null;
  trackingEnabled?: boolean;

  /** `broadcast` or `transactional`, as the API names it. */
  templateKind?: string | null;
  segment?: Segment | null;

  /** The person who drafted it. Never the person allowed to send it. */
  createdBy?: string | null;

  /** The second name, once there is one. */
  approvedBy?: string | null;
};

export type CampaignEmailSettings = {
  subject: string;
  previewText: string;
  fromName: string;
  fromEmail: string;
  replyTo: string;
};

export type CampaignUpdate = CampaignEmailSettings & {
  name: string;
  segment: Segment | null;
  trackingEnabled: boolean;
  revision: number;
};

export type CampaignSaveResult = { ok: true; campaign: Campaign } | { ok: false; error: string };

/**
 * What happened to the messages a campaign wrote.
 *
 * Only meaningful once it has left draft, and the only honest answer to "who
 * has actually been mailed" while a send is in progress. `gone` is everything
 * that has left this system whatever the provider did with it next, which is
 * the number that decides whether cancelling is still worth anything.
 */
export type MessageProgress = {
  total: number;
  pending: number;
  gone: number;
  byStatus: Record<string, number>;
};

export type CampaignRecipientPage = {
  items: {
    id: string;
    email: string;
    firstName: string | null;
    lastName: string | null;
    sentAt: string | null;
  }[];
  page: number;
  pageSize: number;
  total: number;
};

export type RecipientRead =
  | { ok: true; recipients: CampaignRecipientPage }
  | { ok: false; error: string };

export type CampaignAnalytics = {
  sentEmails: number;
  trackedEmails: number;
  clickedEmails: number;
  totalClicks: number;
  clickTrackingEnabled: boolean;
  content: {
    templateName: string;
    subject: string;
    html: string;
    previewText: string | null;
    fromName: string | null;
    fromEmail: string;
    replyTo: string | null;
    isSample: boolean;
  } | null;
  links: {
    destination: string;
    trackedEmails: number;
    clickedEmails: number;
    totalClicks: number;
    lastClickedAt: string | null;
  }[] | null;
  engagement: {
    openTrackedEmails: number;
    openedEmails: number;
    totalOpens: number;
    recordedClicks: number;
    automatedEvents: number;
    firstRecordedAt: string | null;
    daily: { bucket: string; opens: number; clicks: number }[];
    hourly: { bucket: string; opens: number; clicks: number }[];
    browsers: { name: string; count: number }[];
    operatingSystems: { name: string; count: number }[];
    platforms: { name: string; count: number }[];
    countries: { name: string; count: number }[];
  };
};

/**
 * One placeholder the template uses, counted against the people it would go to.
 *
 * `missing` is the number of recipients who carry no value for it, and they
 * are the reason this type exists: a placeholder the segment cannot fill is
 * not an error anywhere — the send succeeds and twelve people read a literal
 * `{{firstName}}`. The only moment anybody can catch that is before the send.
 *
 * `examples` is a handful of the addresses behind `missing`, never the list.
 * These are people, and a screen that dumped four hundred addresses to
 * illustrate a number would be handing out a mailing list to make a point.
 */
export type PlaceholderCoverage = {
  /** The name inside the braces, as the API found it in the template. */
  placeholder: string;
  missing: number;
  total: number;
  examples?: string[];
};

/**
 * One recipient's message, rendered by the same code path a send goes through.
 *
 * Both parts, because both are sent: an inbox that refuses HTML gets `text`,
 * and a preview that only showed the HTML would be checking half of what goes
 * out.
 *
 * `unfilled` is what this particular message still has braces around — the
 * per-person half of the coverage numbers, and the half somebody can actually
 * see happening.
 */
export type Render = {
  email: string;
  subject: string;
  html: string;
  text: string;
  unfilled?: string[];
};

/**
 * What a send would actually do, resolved now.
 *
 * The count is the whole point of the screen and the sample is what makes it
 * checkable: a count of 340 looks the same whether it is the accepted list or
 * everybody who ever started an application, and the addresses are the only
 * thing that tells the two apart.
 */
export type Preview = {
  revision?: number;
  /** People who will actually be mailed. */
  recipientCount: number;

  sample: string[];

  /**
   * Everybody the segment matched, before suppressions.
   *
   * Shown next to recipientCount when they differ, because "412 matched, 400
   * will be sent" is the sentence that lets somebody find the twelve. The API
   * writes suppressed recipients as rows rather than dropping them, so they
   * are findable rather than merely counted.
   */
  segmentSize?: number;
  suppressedCount?: number;

  /**
   * Matching answers there is nobody to mail about.
   *
   * Only ever non-zero for an answer segment, and the reason it is on the
   * screen at all: the form screen counts answers and this one counts people,
   * so "forty responses" and "thirty-one recipients" with nothing in between
   * reads as a bug. Anonymous answers have no person and no address —
   * deliberately, so they could be kept at all — and an answer from somebody
   * who never applied has no application to reach them through.
   */
  unreachableCount?: number;

  /**
   * Why the suppressed ones were suppressed, counted by reason.
   *
   * The keys are the API's own words for a suppression and are shown as it
   * writes them. A screen that translated "bounce" into a friendlier sentence
   * would be maintaining a second, worse copy of a list the sender owns, and
   * the twelve people behind these numbers have to be findable by the name the
   * system actually recorded.
   */
  suppressedByReason?: Record<string, number>;

  /**
   * Everything the API thinks is wrong with this campaign, in its own
   * sentences.
   *
   * Advisory at preview and fatal at send: a template that greets people by a
   * name the segment does not carry is refused by the send, and the person
   * reading this screen is the last one who can still fix it.
   */
  problems?: string[];

  /**
   * Every placeholder the template uses, and how many recipients can fill it.
   *
   * Optional because the API grew it after this screen shipped. Absent and
   * empty mean different things and are shown differently: absent is an API
   * that has not been asked, empty is a template with no placeholders in it.
   */
  placeholderCoverage?: PlaceholderCoverage[];

  /**
   * A few of the messages, rendered.
   *
   * Deliberately a sample — three to five — and not everybody. Four hundred
   * rendered messages is not a thing anybody reads, and the value here is
   * seeing one real message rather than all of them.
   */
  renders?: Render[];
};

/** A form somebody could have answered, for the segment picker. */
export type FormChoice = { id: string; name: string };

/**
 * One question on a form, as the answer picker needs it.
 *
 * `unmatchable` is the API's own sentence about why an audience cannot be
 * chosen by this question's answer, and null when it can. The question is
 * listed either way: somebody hunting for the hardware-track question has to
 * be told that the one they are looking at is the wrong kind, because a list
 * that quietly dropped it reads as the question having been deleted.
 *
 * `values` is what the form declared, never a scan of what people answered —
 * reading several hundred answers is `applications.view_responses`, which the
 * team that builds segments deliberately does not hold. Empty for a typed
 * question, where the organizer types the value instead.
 */
export type FormQuestion = {
  key: string;
  label: string;

  /** The question type, as the API spells it: `radio`, `shortText`, … */
  type: string;
  values: { value: string; label: string }[];
  unmatchable: string | null;
};

/**
 * A form's questions, and which version they are.
 *
 * The version is shown rather than implied. These are the questions being
 * answered now, so a question removed in a later version is not offered — and
 * the people who answered it before it went are excluded by that. Saying which
 * version this is makes that a visible limitation instead of a silent one.
 */
export type FormQuestionsRead =
  | { ok: true; version: number | null; questions: FormQuestion[] }
  | { ok: false; error: string };

/**
 * The application statuses, exactly as the database constrains them.
 *
 * Copied from the check constraint on applications.applications rather than
 * invented here. A status this list has that the database does not is a
 * segment that resolves to nobody; one it is missing is a group that cannot be
 * mailed at all.
 */
export const APPLICANT_STATUSES: { value: string; label: string }[] = [
  { value: "incomplete", label: "Incomplete" },
  { value: "submitted", label: "Submitted" },
  { value: "under_review", label: "Under review" },
  { value: "accepted", label: "Accepted" },
  { value: "rejected", label: "Rejected" },
  { value: "waitlisted", label: "Waitlisted" },
  { value: "confirmed", label: "Confirmed" },
  { value: "declined", label: "Declined" },
  { value: "expired", label: "Expired" },
  { value: "checked_in", label: "Checked in" },
  { value: "withdrawn", label: "Withdrawn" },
];

/** The word for a status, or the status itself if it is one this list has not met. */
export function statusLabel(value: string): string {
  return APPLICANT_STATUSES.find((s) => s.value === value)?.label ?? value;
}

/**
 * Who a campaign goes to, in a line.
 *
 * The form's name where it is known and its id where it is not, because a
 * campaign can outlive the form it segmented on and an id is still true.
 */
export function describeSegment(
  segment: Segment | null | undefined,
  formName?: string | null,
): string {
  if (!segment) {
    return "No segment";
  }

  if (segment.type === "applicationStatus") {
    return `Applicants · ${segment.statuses.map(statusLabel).join(", ")}`;
  }

  if (segment.type === "formRespondents") {
    return `Form respondents · ${formName ?? segment.formId}`;
  }

  // The question's key rather than its label, because the label is only known
  // while the question still exists and the key is what was stored. Read as a
  // sentence it is still the right sentence a year later.
  if (segment.type === "formAnswer") {
    return `Answered ${segment.question} = ${segment.value} · ${formName ?? segment.formId}`;
  }

  const recipients = listRecipients(segment);
  const named = recipients.filter(r => r.firstName || r.lastName).length;

  return named > 0
    ? `Address list · ${recipients.length} (${named} with names)`
    : `Address list · ${recipients.length}`;
}

const campaignDate = new Intl.DateTimeFormat("en-US", {
  month: "short", day: "numeric", year: "numeric", timeZone: "UTC",
});
const campaignTime = new Intl.DateTimeFormat("en-US", {
  hour: "numeric", minute: "2-digit", timeZone: "UTC", timeZoneName: "short",
});

export function when(iso: string | null): string {
  if (typeof iso !== "string" || iso.length < 16) {
    return "—";
  }

  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? "—" : `${campaignDate.format(date)} · ${campaignTime.format(date)}`;
}
