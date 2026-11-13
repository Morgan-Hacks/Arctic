using System.Globalization;
using System.Text;
using System.Text.Json;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Segments;
using MorganHacks.Applications.Services;
using MorganHacks.Lark.Data.Data;
using MorganHacks.Lark.Data.Domain;

// Just the types, not the namespace: Forms and Segments each declare a
// ColumnKind, and importing both makes every use of it ambiguous.
using Form = MorganHacks.Applications.Forms.Form;
using FormVersion = MorganHacks.Applications.Forms.FormVersion;
using AnswerColumns = MorganHacks.Applications.Forms.AnswerColumns;
using AnswerStorage = MorganHacks.Applications.Forms.AnswerStorage;
using FieldType = MorganHacks.Applications.Forms.FieldType;

namespace MorganHacks.Api;

/// <summary>
/// The <c>{{placeholders}}</c> a broadcast can fill in, and what fills them.
/// </summary>
/// <remarks>
/// One list, read four ways. <see cref="Values"/> builds what
/// <see cref="TemplateRenderer"/> is handed for one recipient;
/// <see cref="Fillable"/> says which of them a segment carries for everybody;
/// <see cref="Unfilled"/> says which of them one person has nothing for; and
/// the two placeholder endpoints hand the names to the template editor so it
/// can offer them.
/// <para>
/// One list rather than four, because the failure of four is silent. An editor
/// offering a placeholder the renderer has never heard of produces a template
/// that reads fine, passes review, and refuses at send — so the person who
/// wrote it finds out from the approver who could not send it, which is the
/// one place in this system where being told late costs somebody else's
/// afternoon.
/// </para>
/// <para>
/// The list itself is not written here. It is
/// <see cref="ApplicantColumns.Mergeable"/> — the columns
/// <c>applications.applications</c> actually has — so a placeholder exists
/// because a column does, and a name nobody can fill cannot be offered because
/// there is nothing to derive it from. What this file adds is the two things
/// that are the mail's business rather than the table's: what a column is
/// called in a template, and how its value reads in a sentence.
/// </para>
/// <para>
/// Derived from a declared list rather than from the live schema. Reading
/// <c>information_schema</c> on every request would be a query on the editor's
/// keystroke and, worse, would let a migration change this API without anybody
/// deciding to. The declaration is checked against the schema by a test
/// instead, so the two cannot drift and the failure lands in CI rather than in
/// a send.
/// </para>
/// </remarks>
public static class MergeFields
{
    /// <summary>
    /// The headings the editor groups placeholders under.
    /// </summary>
    /// <remarks>
    /// Words an organizer writing an email would use, not words from this side
    /// of the screen. <c>Links</c> rather than <c>System</c> for the same
    /// reason the names under it are <c>{{link.portal}}</c> rather than
    /// <c>{{system.portalUrl}}</c>: the person reading the menu is looking for
    /// a link to the portal.
    /// </remarks>
    public static class Groups
    {
        public const string Applicant = "About the person";
        public const string Event = "The event";
        public const string Form = "The form";
        public const string Links = "Links";
        public const string Saved = "Saved values";

        /// <summary>
        /// What each recipient answered the form this template names.
        /// </summary>
        /// <remarks>
        /// Its own heading rather than more rows under <see cref="Form"/>,
        /// although both appear and disappear together. The three under
        /// <c>The form</c> are the same sentence for everybody in the send —
        /// its name, its link, when it closes — and these are different for
        /// every person who receives the mail. An author scanning a menu for
        /// "what did they say" is looking for a different thing from an author
        /// looking for "where is the form", and one heading over both would
        /// make a per-recipient value look like a fixed one.
        /// </remarks>
        public const string Answers = "Their answers";
    }

    /// <summary>
    /// One placeholder: what to type, what it says, and where it comes from.
    /// </summary>
    /// <remarks>
    /// <see cref="Column"/> is null for anything that is not an answer on an
    /// application. That is the whole difference between the two kinds today —
    /// a column-backed field is looked up per recipient, and everything else
    /// is the same for everybody in the send — and it is why
    /// <see cref="Values"/> takes the configuration as well as the member.
    /// <para>
    /// For a column-backed field <see cref="Name"/> is derived rather than
    /// declared — see <see cref="NameFor"/> — so there is no second spelling
    /// of a column to keep in step with the first. A namespaced name has
    /// nothing to derive from and is written out, which is why the ones below
    /// sit next to the value that fills them.
    /// </para>
    /// </remarks>
    public sealed record MergeField(
        string Name,
        string Group,
        string Description,
        bool OnAddressLists,
        ApplicantColumn? Column = null);

    /// <summary>
    /// Our own addresses, which are the same for everybody in a send.
    /// </summary>
    /// <remarks>
    /// These exist because the alternative is somebody typing
    /// <c>https://www.morganhacks.com/portal</c> into a template, which is a
    /// row in a database — so the same template on staging links to
    /// production. The preview looks right, the test send looks right, and the
    /// only way to find out is to click through and notice which site you
    /// landed on.
    /// <para>
    /// <see cref="MergeField.OnAddressLists"/> is true for all of them: they
    /// do not depend on who is receiving the mail, so a typed list of
    /// addresses can fill them as well as a segment can.
    /// </para>
    /// <para>
    /// The sign-in link is deliberately not here. It is per-recipient and
    /// transactional-only, so offering it in a broadcast editor would be
    /// offering a name that cannot be filled — and saying so needs the
    /// catalogue to carry an audience, which is its own piece of work.
    /// <c>{{link}}</c> keeps working exactly as it does now in the meantime.
    /// </para>
    /// </remarks>
    private static readonly (string Name, string Description, Func<IConfiguration, string> Value)[] Links =
    [
        ("link.portal", "The hacker portal, where somebody checks their application.",
            Origins.Portal),
        ("link.forms", "The site the public forms are served from.",
            Origins.Forms),
        ("link.console", "The organizer console. For mail to the team, not to applicants.",
            Origins.Console),

        // The one link here that is not an address this system is configured
        // with. It renders to EmailUnsubscribe.Placeholder, which is a
        // sentinel rather than a URL, and lark swaps it for that recipient's
        // own address in UnsubscribeStore just before the send -- because the
        // address identifies the person and rendering happens once for the
        // whole campaign.
        //
        // The consequence is visible and worth expecting: a preview shows the
        // sentinel in the href rather than a working link, because the preview
        // renders through the same call the send does and there is genuinely
        // no recipient yet.
        //
        // It is offered here rather than left as a raw {$unsubscribe_link}
        // nobody could discover: the brace syntax is different from every
        // other placeholder in the editor, so it never appeared in the {{
        // menu and the only way to use it was to know it existed.
        ("link.unsubscribe",
            "Where this person stops receiving broadcasts. Fills in per recipient.",
            _ => EmailUnsubscribe.Placeholder),
    ];

    /// <summary>
    /// The season itself: its name, its dates, how many are coming.
    /// </summary>
    /// <remarks>
    /// Written out rather than derived from the columns of
    /// <c>applications.events</c>, which is the opposite of what
    /// <see cref="ApplicantColumns"/> does and is a deliberate difference. That
    /// catalogue is derived because it is wide, changes with migrations, and
    /// its whole risk is a column quietly becoming mailable. This one is seven
    /// fields on a table with one row a year, and every one of them needs a
    /// sentence and a format that the column type cannot supply —
    /// <c>registration_closes_at</c> is a <c>timestamptz</c> like three others
    /// and the only one an applicant is ever shown.
    /// <para>
    /// Dates read through <see cref="EventZone"/>, which is the reason these
    /// exist at all now and did not before. PR #71 withheld
    /// <c>rsvp_deadline</c> from the applicant catalogue in as many words —
    /// "nothing here knows the event's timezone, so a midnight deadline
    /// rendered in UTC lands on the wrong calendar day for exactly the people
    /// it matters to". The zone is known now, and the sentence these render is
    /// the one the console and the public form already show.
    /// </para>
    /// <para>
    /// A date nobody has set yet returns null, which keeps it out of the
    /// dictionary, which leaves the placeholder standing and lets the
    /// campaign's coverage check refuse the send. That is the case worth
    /// getting right: an email promising a deadline the team has not agreed is
    /// worse than one that cannot be sent.
    /// </para>
    /// </remarks>
    private static readonly (string Name, string Description, Func<EventDetail, string?> Value)[] Season =
    [
        ("event.name", "The event's name, as the console spells it.",
            e => string.IsNullOrWhiteSpace(e.Name) ? null : e.Name),

        ("event.startsAt", "When the event starts.",
            e => Moment(e.StartsAt)),
        ("event.endsAt", "When the event ends.",
            e => Moment(e.EndsAt)),

        ("event.registrationOpensAt", "When applications open.",
            e => Moment(e.RegistrationOpensAt)),
        ("event.registrationClosesAt", "The application deadline.",
            e => Moment(e.RegistrationClosesAt)),
        ("event.decisionsAnnouncedAt", "When applicants hear back.",
            e => Moment(e.DecisionsAnnouncedAt)),

        ("event.capacity", "How many people the event can take.",
            e => e.Capacity?.ToString(CultureInfo.InvariantCulture)),
    ];

    /// <summary>
    /// The form this email is about, where a template names one.
    /// </summary>
    /// <remarks>
    /// Three fields and no more. There is no <c>opensAt</c> because
    /// <c>applications.forms</c> has no such column — a form is reachable from
    /// the moment it is published, and the only date it carries is the one it
    /// closes on.
    /// <para>
    /// The link is built rather than stored, from the forms origin and the
    /// form's own code, so it is right in each environment for the same reason
    /// <see cref="Links"/> is. A share URL typed into a template body is a
    /// production URL on staging.
    /// </para>
    /// </remarks>
    private static readonly (string Name, string Description, Func<Form, IConfiguration, string?> Value)[] Paper =
    [
        ("form.link", "The link somebody opens to fill the form in.",
            (form, config) => $"{Origins.Forms(config)}/{form.Code}"),

        ("form.name", "The form's name, as the console spells it.",
            (form, _) => string.IsNullOrWhiteSpace(form.Name) ? null : form.Name),

        ("form.closesAt", "When the form stops accepting answers.",
            (form, _) => Moment(form.ClosesAt)),
    ];

    /// <summary>A date as a person reads it, or null for one nobody has set.</summary>
    private static string? Moment(DateTimeOffset? instant) =>
        instant is { } set ? EventZone.Readable(set) : null;

    /// <summary>The prefix every saved value is offered under.</summary>
    /// <remarks>
    /// The reason a saved name may not contain a dot, enforced by the check
    /// constraint in <c>0045</c>: with the prefix reserved and the name unable
    /// to hold one, a saved value cannot collide with a built-in name however
    /// either set grows.
    /// </remarks>
    public const string SavedPrefix = "saved.";

    /// <summary>The prefix every answer to the bound form is offered under.</summary>
    /// <remarks>
    /// Three segments rather than two, and the third is what makes the set
    /// safe. A question keyed <c>link</c> or <c>name</c> is an ordinary thing
    /// for a form to ask, and <c>{{form.link}}</c> under
    /// <c>{{form.&lt;key&gt;}}</c> would be that question quietly taking over
    /// the share URL — or being taken over by it, depending on which loop ran
    /// last. Under this prefix it is <c>{{form.answer.link}}</c> and the two
    /// cannot meet, however many questions a form grows.
    /// <para>
    /// Structural rather than checked, like <see cref="SavedPrefix"/>:
    /// <c>DraftKeys</c> holds a question key to
    /// <c>^[a-z][a-z0-9_]{0,62}$</c>, so a key can never contain a dot and can
    /// never climb back out of this namespace. The same shape is also why
    /// every name here matches <c>TemplateRenderer</c>'s <c>[\w.]+</c> without
    /// anything having to escape it.
    /// </para>
    /// </remarks>
    public const string AnswerPrefix = "form.answer.";

    /// <summary>
    /// The catalogue, plus whatever somebody has saved and whatever the bound
    /// form asks.
    /// </summary>
    /// <remarks>
    /// <see cref="All"/> cannot be a static list any more: saved values are
    /// rows, so the catalogue is only complete once they have been read. Every
    /// caller that offers names to an author or checks a name against them
    /// goes through here; <see cref="All"/> remains what the declaration
    /// alone knows, which is what the schema test compares.
    /// <para>
    /// The two groups this file does not declare — the form's questions and
    /// the saved rows — come after the ones it does, which is the only thing
    /// about the ordering that is a decision. The editor draws a heading each
    /// time the group changes, so what matters is that a group's names are
    /// contiguous rather than where the group sits; and the declared ones keep
    /// the order <see cref="All"/> gives them, so a group added there is
    /// offered here without this function being touched.
    /// </para>
    /// <para>
    /// <paramref name="answers"/> is narrowed by the same
    /// <paramref name="aboutAForm"/> the <c>form.</c> group is, because it is
    /// the same fact: a template that names no form has no questions to echo,
    /// and offering a name the send cannot fill is what this file exists to
    /// stop.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MergeField> Including(
        IEnumerable<SavedValue> saved,
        bool aboutAForm = false,
        FormAnswers? answers = null) =>
    [
        .. aboutAForm ? All : All.Where(field => field.Group != Groups.Form),
        .. aboutAForm ? Asked(answers ?? FormAnswers.None) : [],
        .. saved.Select(value => new MergeField(
            SavedPrefix + value.Name,
            Groups.Saved,
            value.Description ?? "Saved by an organizer.",
            OnAddressLists: true)),
    ];

    /// <summary>One placeholder per question on the form a template names.</summary>
    /// <remarks>
    /// <see cref="MergeField.OnAddressLists"/> is false for all of them, and
    /// that is the whole reason this is not simply three more rows of
    /// <see cref="Paper"/>. An answer belongs to one person, and a typed list
    /// of addresses is mentors and sponsors nobody has an answer for — so the
    /// name is not offered there rather than offered and then refused at send.
    /// <para>
    /// The description is the question's own wording, which is the obvious
    /// choice and also the only honest one: the editor shows it beside the name
    /// so an author can tell <c>{{form.answer.q1}}</c> from
    /// <c>{{form.answer.q2}}</c>, and nothing else about the question says
    /// which is which.
    /// </para>
    /// </remarks>
    private static IEnumerable<MergeField> Asked(FormAnswers answers) =>
        answers.Questions.Select(question => new MergeField(
            AnswerPrefix + question.Key,
            Groups.Answers,
            Wording(question.Label),
            OnAddressLists: false));

    /// <summary>
    /// A question's own wording, short enough to sit in a menu.
    /// </summary>
    /// <remarks>
    /// MLH's data-sharing agreement is sixty words, and a form can ask it. A
    /// description is laid out as one line beside a name, so the long ones are
    /// cut — the same move <c>SubmissionValidation.Shorten</c> makes when it
    /// quotes a label back inside a complaint, at a looser bound because this
    /// one is a row of its own rather than the middle of a sentence.
    /// <para>
    /// The fallback is unreachable while publishing refuses a question with no
    /// wording, and is here because a name with nothing beside it is a name
    /// somebody has to guess at and that is worse than a dull sentence.
    /// </para>
    /// </remarks>
    private static string Wording(string label) =>
        string.IsNullOrWhiteSpace(label) ? "A question on the form."
        : label.Length <= 80 ? label
        : label[..77].TrimEnd() + "…";

    /// <summary>
    /// The questions on the form a template names whose answers a message may
    /// carry.
    /// </summary>
    /// <remarks>
    /// The <b>published</b> version's, which is what is being answered now.
    /// <c>AnswerQuestions</c> gives the reasoning at length and it applies
    /// unchanged here: a question that existed in version two and is gone from
    /// version five still has answers sitting in older rows, and the people who
    /// gave them are left with the placeholder standing. Offering every
    /// question that ever existed is the more complete list and a much longer
    /// one, and it is not what this does.
    /// <para>
    /// Narrowed to the answers that are in the answer set at all — see
    /// <see cref="AnswerQuestions.InTheAnswerSet"/>, which is the one place
    /// that decides where an answer lives. The case that matters is an
    /// application form's promoted question: <c>school</c> is answered into
    /// <c>applications.school</c> and not into the jsonb, so
    /// <c>{{form.answer.school}}</c> could never be filled — and
    /// <c>{{school}}</c> already exists for it, two rows up this very menu.
    /// </para>
    /// <para>
    /// Empty for a form nobody has published, which still offers the
    /// <c>form.</c> group: the link and the name are properties of the form and
    /// do not wait on a version. A form with no questions yet and a form with
    /// no answerable ones look the same from here, and both are honest.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Why a form's questions did not all become placeholders.
    /// </summary>
    /// <remarks>
    /// Counts rather than names, because this answers one question an author
    /// has while looking at a short menu: whether the questions were considered
    /// at all. A standard application form is the case that needs it -- every
    /// question on it is a column, so none of them become
    /// <c>{{form.answer.*}}</c> and the menu gains three fields and nothing
    /// else. That is correct and reads exactly like a broken picker.
    /// <para>
    /// <c>Withheld</c> is counted apart from <c>AlreadyFields</c> because they
    /// are different sentences. One says the answer is already offered under
    /// another name; the other says it is deliberately not offered at all, for
    /// the reasons <see cref="ApplicantColumns.Withheld"/> gives -- and an
    /// author told only "already a field" would go looking for a name that is
    /// never going to be there.
    /// </para>
    /// </remarks>
    public sealed record AnswerSummary(
        int Offered, int AlreadyFields, int Withheld, int Files);

    /// <summary>Classifies every published question of the chosen form.</summary>
    public static AnswerSummary SummarizeAnswers(
        Form? paper, FormVersion? published, IReadOnlyList<AnswerQuestion> offered)
    {
        if (paper is null || published is null)
        {
            return new AnswerSummary(0, 0, 0, 0);
        }

        var already = 0;
        var withheld = 0;
        var files = 0;

        foreach (var field in published.Fields)
        {
            // Not a question. A section is a page heading, and counting it
            // would make the arithmetic on screen fail to add up.
            if (field.Type == FieldType.Section)
            {
                continue;
            }

            if (field.Type == FieldType.File)
            {
                files++;
                continue;
            }

            if (!paper.IsApplication
                || field.Storage != AnswerStorage.Column
                || !AnswerColumns.TryKindOf(field.Column, out _))
            {
                continue;
            }

            if (ApplicantColumns.Withheld.ContainsKey(field.Column!))
            {
                withheld++;
            }
            else
            {
                already++;
            }
        }

        return new AnswerSummary(offered.Count, already, withheld, files);
    }

    public static IReadOnlyList<AnswerQuestion> QuestionsOn(
        Form? paper, FormVersion? published) =>
        paper is null || published is null
            ? []
            : AnswerQuestions.On(
                paper.IsApplication,
                [.. published.Fields.Where(field =>
                    AnswerQuestions.InTheAnswerSet(paper.IsApplication, field))]);

    /// <summary>Every placeholder the declaration alone knows.</summary>
    public static readonly IReadOnlyList<MergeField> All =
    [
        .. ApplicantColumns.Mergeable.Select(column => new MergeField(
            NameFor(column.Column),
            Groups.Applicant,
            column.Description,
            column.OnAddressLists,
            column)),

        .. Season.Select(field => new MergeField(
            field.Name, Groups.Event, field.Description, OnAddressLists: true)),

        .. Paper.Select(field => new MergeField(
            field.Name, Groups.Form, field.Description, OnAddressLists: true)),

        .. Links.Select(link => new MergeField(
            link.Name, Groups.Links, link.Description, OnAddressLists: true)),
    ];

    /// <summary>
    /// The one place a column name becomes a placeholder name.
    /// </summary>
    /// <remarks>
    /// <c>first_name</c> is <c>{{firstName}}</c> because this says so, and
    /// there is nowhere else that could say otherwise. A declared name beside
    /// each column would be a second thing to get right, and the way it goes
    /// wrong is a placeholder the editor offers under one spelling and the
    /// send looks up under another.
    /// </remarks>
    public static string NameFor(string column)
    {
        var parts = column.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var name = new StringBuilder(parts[0]);

        foreach (var part in parts.Skip(1))
        {
            name.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        }

        return name.ToString();
    }

    /// <summary>
    /// The merge values a segment can supply for one recipient.
    /// </summary>
    /// <remarks>
    /// A field with nothing behind it is left out rather than written in
    /// empty, so <see cref="TemplateRenderer"/> leaves the placeholder
    /// standing and <see cref="Unfilled"/> can see that it did. That is what
    /// makes the gap countable: a row autosaved before somebody typed their
    /// name has an email and no first name, and the template greeting them
    /// would reach them as "Hi {{firstName}},".
    /// </remarks>
    public static Dictionary<string, string> Values(
        SegmentMember member,
        IConfiguration config,
        EventDetail? season = null,
        IReadOnlyList<SavedValue>? saved = null,
        Form? paper = null,
        FormAnswers? answers = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        // Everything that is the same for the whole send first, so that if a
        // column-backed name ever collided with one the person's own answer
        // would win. It cannot collide today — NameFor produces no dots — and
        // writing it in this order means it stays harmless if that changes.
        foreach (var link in Links)
        {
            values[link.Name] = link.Value(config);
        }

        // Null where the template names no form, or names one that has since
        // been deleted or removed. Every form placeholder then stands and the
        // coverage check refuses the send, which is the right failure: a dead
        // link inside an approved broadcast is worse than one that will not go.
        if (paper is not null)
        {
            foreach (var field in Paper)
            {
                if (field.Value(paper, config) is { } value)
                {
                    values[field.Name] = value;
                }
            }
        }

        foreach (var value in saved ?? [])
        {
            values[SavedPrefix + value.Name] = value.Value;
        }

        // Null when there is no event yet, which a fresh database has. Every
        // event placeholder is then left standing and the coverage check
        // refuses the send, which is the right answer: a broadcast that names
        // a season nobody has created is not one to guess at.
        if (season is not null)
        {
            foreach (var field in Season)
            {
                if (field.Value(season) is { } value)
                {
                    values[field.Name] = value;
                }
            }
        }

        foreach (var field in All)
        {
            if (field.Column is not { } column)
            {
                continue;
            }

            if (member.Fields.TryGetValue(column.Column, out var stored)
                && Reads(stored, column) is { } value)
            {
                values[field.Name] = value;
            }
        }

        // Last, beside the columns, because an answer and a column are the two
        // things in here that belong to one recipient rather than to the whole
        // send. Nothing above can be shadowed by one all the same:
        // AnswerPrefix is reserved and a question key cannot hold a dot, so
        // form.answer.link is not form.link and can never become it.
        //
        // Null where the template names no form, names one that is gone, or
        // names one nobody has published. All three leave every answer
        // placeholder standing, which the coverage check turns into a refusal —
        // the same failure, for the same reason, as the dead-link case above.
        if (answers is not null)
        {
            // The questions, not the keys in the row. A form's author can add a
            // question at any time and an answer can be stored under a key the
            // catalogue never offered; filling one of those would be the one
            // list read two ways, which is the failure this file is about.
            var given = answers.Of(member.Email);

            foreach (var question in answers.Questions)
            {
                if (given.TryGetValue(question.Key, out var answer)
                    && Reads(answer) is { } value)
                {
                    values[AnswerPrefix + question.Key] = value;
                }
            }
        }

        return values;
    }

    /// <summary>
    /// How one column's value reads inside a sentence, or null for nothing.
    /// </summary>
    /// <remarks>
    /// Per type, because the defaults are wrong in ways somebody only notices
    /// after four hundred copies have left: a <c>boolean</c> stringifies as
    /// "True", which is a word no email has ever contained, and a year through
    /// a thousands separator is "2,027".
    /// <para>
    /// Null is the case that matters, and it covers three things that are the
    /// same thing to a reader: the column is null, the column is text nobody
    /// typed into, or the segment did not carry the column at all. All of them
    /// return null, which keeps the value out of the dictionary, which is what
    /// <see cref="Unfilled"/> counts and what the preview reports before
    /// anybody can send it.
    /// </para>
    /// <para>
    /// The throw is unreachable while the divergence test passes: it fires
    /// only if a column's declared kind stops matching the type the table
    /// hands back, and that test fails first, in CI, with the column named.
    /// Throwing rather than rendering something is deliberate all the same —
    /// the alternative is guessing at a value that goes out under our name.
    /// </para>
    /// </remarks>
    private static string? Reads(object? stored, ApplicantColumn column) =>
        (column.Kind, stored) switch
        {
            (_, null) => null,
            (ColumnKind.Text, string text) => string.IsNullOrWhiteSpace(text) ? null : text,
            (ColumnKind.Integer, int number) => number.ToString(CultureInfo.InvariantCulture),
            (ColumnKind.Boolean, bool yes) => yes ? "yes" : "no",
            _ => throw new InvalidOperationException(
                $"applications.applications.{column.Column} is declared "
                + $"{column.Kind} and came back as "
                + $"{stored.GetType().Name}."),
        };

    /// <summary>
    /// How one form answer reads inside a sentence, or null for nothing.
    /// </summary>
    /// <remarks>
    /// The same job as <see cref="Reads(object?, ApplicantColumn)"/> over a
    /// different set of types, and the same answers where they overlap: a
    /// boolean is "yes" and not "True", a number carries no thousands
    /// separator. An organizer who merges a tick on one form and a boolean
    /// column on another must not get two different words for it.
    /// <para>
    /// Read off the JSON shape rather than off the question's declared type,
    /// which is not laxity but the only thing that is actually true of the
    /// stored value. <c>applications.responses</c> is written through a
    /// normaliser that types the answer; <c>form_submissions.answers</c> holds
    /// what the browser posted, where a number input posts <c>"21"</c> and a
    /// checkbox posts <c>true</c> — <c>PostgresSegmentResolver.Matches</c> has
    /// the same paragraph for the same reason. A renderer that trusted the
    /// question would print the raw JSON for half the forms in the table.
    /// </para>
    /// <para>
    /// The number is handed back as it was stored rather than parsed and
    /// reformatted. A JSON number has no separators and no culture in it
    /// already, so a round trip through <see cref="decimal"/> could only lose
    /// something — <c>3.50</c> becoming <c>3.5</c> on an answer somebody typed
    /// as a price.
    /// </para>
    /// <para>
    /// Trimmed, unlike the text column above. That one is written by the submit
    /// path's normaliser and this one is what a browser posted, so a space at
    /// either end is a real possibility — and it is visible in the middle of a
    /// sentence rather than merely untidy.
    /// </para>
    /// <para>
    /// Null for an object, which is the one shape with no answer in it: a file
    /// question stores where the upload went.
    /// <see cref="AnswerQuestions.InTheAnswerSet"/> already keeps those out of
    /// the catalogue, so this is the second line rather than the first — and it
    /// is here because an upload id rendered into a body is
    /// <c>applications.resume_key</c> leaving the schema by another door.
    /// </para>
    /// </remarks>
    private static string? Reads(JsonElement answer) => answer.ValueKind switch
    {
        JsonValueKind.String => answer.GetString() is { } text
                                && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null,

        JsonValueKind.Number => answer.GetRawText(),
        JsonValueKind.True => "yes",
        JsonValueKind.False => "no",
        JsonValueKind.Array => Chosen(answer),
        _ => null,
    };

    /// <summary>
    /// A multi-select as a person would write it, or null for nothing picked.
    /// </summary>
    /// <remarks>
    /// "Hardware, design and games", not <c>["hardware","design","games"]</c>.
    /// A checkbox question is one answer with several parts and the array is
    /// how it is stored; an email that showed the brackets would be showing the
    /// reader our schema, and quoting it back inside a sentence is the entire
    /// point of the feature.
    /// <para>
    /// No comma before the "and". One is defensible and this reads as more
    /// people write it; what matters is that there is one rule rather than a
    /// decision per template.
    /// </para>
    /// <para>
    /// Each element goes through <see cref="Reads(JsonElement)"/>, so a list of
    /// numbers or of ticks is not a special case and a blank element is dropped
    /// rather than left as a hole between two commas. An empty array is null,
    /// which is the same answer <c>SubmissionValidation.IsAnswered</c> gives
    /// it: a checkbox group with nothing ticked is the absence of an answer and
    /// not an answer of none.
    /// </para>
    /// </remarks>
    private static string? Chosen(JsonElement picked)
    {
        var parts = new List<string>();

        foreach (var item in picked.EnumerateArray())
        {
            if (Reads(item) is { } part)
            {
                parts.Add(part);
            }
        }

        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }

    /// <summary>The placeholders a segment can fill for everybody in it.</summary>
    public static IReadOnlySet<string> Fillable(
        Segment segment,
        IReadOnlyList<SavedValue>? saved = null,
        bool aboutAForm = false,
        FormAnswers? answers = null) =>
        new HashSet<string>(
            For(segment, saved, aboutAForm, answers).Select(field => field.Name),
            StringComparer.Ordinal);

    /// <summary>The fields a segment can fill, described for the editor.</summary>
    /// <remarks>
    /// Narrowed rather than annotated. A list that offered <c>{{firstName}}</c>
    /// beside a note saying this segment cannot fill it is a list somebody
    /// clicks anyway.
    /// <para>
    /// <paramref name="aboutAForm"/> is the same argument one level up. A
    /// template that names no form cannot fill <c>{{form.link}}</c>, so it is
    /// not offered — rather than offered and then refused at send by somebody
    /// who did not write it.
    /// </para>
    /// <para>
    /// A typed list of addresses drops the answers along with the applicant
    /// columns, and by the same rule rather than by a second one: an answer is
    /// one person's, and these recipients are people this system has no answers
    /// for. See <see cref="Asked"/>, where that is one false flag.
    /// </para>
    /// </remarks>
    public static IEnumerable<MergeField> For(
        Segment segment,
        IReadOnlyList<SavedValue>? saved = null,
        bool aboutAForm = false,
        FormAnswers? answers = null)
    {
        var catalogue = Including(saved ?? [], aboutAForm, answers);

        return segment is Segment.Addresses
            ? catalogue.Where(field => field.OnAddressLists)
            : catalogue;
    }

    /// <summary>
    /// Which of the placeholders a template asks for this recipient has
    /// nothing to fill.
    /// </summary>
    /// <remarks>
    /// Sorted, because these are read out on a screen and by an assertion, and
    /// both want the same order twice.
    /// <para>
    /// Every argument is forwarded to <see cref="Values"/> and that is the
    /// whole of this function. It took <paramref name="paper"/> and dropped it
    /// before passing it on, which made every <c>{{form.*}}</c> placeholder
    /// unfilled for everybody — so a template that named a form and used its
    /// link was counted as a gap for the entire segment and refused at send,
    /// with nothing on the screen to explain why. The coverage check and the
    /// render have to measure what the send will actually write, so this list
    /// must come from one call and never from a second one that agrees today.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Unfilled(
        IReadOnlySet<string> wanted,
        SegmentMember member,
        IConfiguration config,
        EventDetail? season = null,
        IReadOnlyList<SavedValue>? saved = null,
        Form? paper = null,
        FormAnswers? answers = null)
    {
        var values = Values(member, config, season, saved, paper, answers);

        return wanted.Where(placeholder => !values.ContainsKey(placeholder))
                     .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
                     .ToList();
    }
}
