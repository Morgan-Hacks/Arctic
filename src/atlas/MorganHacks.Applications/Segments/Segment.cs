using System.Globalization;
using System.Text.Json;
using MorganHacks.Applications.Domain;

namespace MorganHacks.Applications.Segments;

/// <summary>
/// Who a broadcast is aimed at.
/// </summary>
/// <remarks>
/// Four shapes, deliberately, and every one of them answers a question the
/// registration team actually asks — "tell everyone we accepted", "tell
/// everyone who filled in the mentor form", "tell everyone who said they want
/// a hardware track", "tell these four people". None of them is a query
/// builder.
/// <para>
/// A query builder is the obvious next step and it is the wrong one. It turns
/// a stored segment into a stored program, so reading it back a month later
/// means re-implementing the evaluator to know what it meant; it makes every
/// column of <c>applications.*</c> part of the API, so the schema can no
/// longer change; and it hands somebody the ability to compose a filter nobody
/// reviewed into several hundred emails that cannot be recalled. A named shape
/// can be read as a sentence, which is the property that matters when the
/// question is "who exactly did we email".
/// </para>
/// <para>
/// <see cref="FormAnswer"/> is the one that comes closest to the line, so it
/// is worth saying where the line is. It carries one question and one value
/// and the only operator it has is equality, so there is no operator to
/// choose, no two criteria to combine and nothing to nest. "Answered
/// <em>track</em> with <em>hardware</em>" is still one sentence; "answered
/// track with hardware OR shirt size with large, unless graduating before
/// 2027" is the program, and none of the four can express it.
/// </para>
/// <para>
/// Parsed from JSON rather than bound by the framework because this is what
/// gets stored, verbatim, in <c>notify.campaigns.segment</c>. The stored
/// document has to survive being read by something that is not this class, so
/// it is a plain tagged object rather than whatever a serializer happens to
/// emit for a hierarchy.
/// </para>
/// </remarks>
public abstract record Segment
{
    /// <summary>The discriminator, as it is written in the stored document.</summary>
    public abstract string Type { get; }

    /// <summary>
    /// Everyone whose application on one event is in one of these states.
    /// </summary>
    /// <remarks>
    /// The decision email, the RSVP reminder, the "you are on the waitlist"
    /// note. Several statuses rather than one because the useful segments are
    /// unions — accepted and confirmed together are "people who are coming or
    /// might be" — and asking somebody to send the same announcement three
    /// times is asking for it to go out twice to somebody who moved between
    /// two of them.
    /// </remarks>
    public sealed record InStatus(Guid EventId, IReadOnlyList<ApplicationStatus> Statuses) : Segment
    {
        public override string Type => "applicationStatus";
    }

    /// <summary>
    /// Everyone who submitted a given form.
    /// </summary>
    /// <remarks>
    /// Named by form rather than by event, because "everyone who answered the
    /// mentor sign-up" is the thing somebody means and it is not the same set
    /// as everyone on the event.
    /// <para>
    /// Only an application form has respondents today. A survey's answers are
    /// refused at submit — <c>PublicFormEndpoints</c> answers 501 rather than
    /// accepting and dropping them — so there is genuinely nobody to resolve,
    /// and this resolves to nothing rather than quietly returning the
    /// applications sitting on the same event.
    /// </para>
    /// </remarks>
    public sealed record FormRespondents(Guid FormId) : Segment
    {
        public override string Type => "formRespondents";
    }

    /// <summary>
    /// Everyone who answered one question on one form with one value.
    /// </summary>
    /// <remarks>
    /// The thing <see cref="FormRespondents"/> could not say. "Everyone who
    /// filled in the interest survey" is four hundred people and "everyone who
    /// said they want a hardware track" is the thirty-one worth asking about
    /// the hardware track, and the second is the mail somebody actually wants
    /// to send.
    /// <para>
    /// <b>This is not a merge field and nothing about the answer reaches the
    /// message.</b> It is a WHERE clause choosing recipients, so the answer
    /// never leaves <c>applications.*</c> and never lands in
    /// <c>notify.messages</c> — which is why none of the reasoning that
    /// withheld <c>dietary_needs</c> from the merge catalogue applies to it.
    /// What gets stored is the criterion, in <c>notify.campaigns.segment</c>,
    /// beside the criteria already stored there.
    /// </para>
    /// <para>
    /// <see cref="Question"/> is a <c>FormField.Key</c> and
    /// <see cref="Value"/> is the answer as the form stores it — an option's
    /// <c>FieldOption.Value</c> for a choice, the text for a short answer,
    /// <c>true</c> or <c>false</c> for an agreement. Both are kept as written
    /// rather than resolved to a label: a label can be reworded later and the
    /// stored segment would then name a question nobody asked.
    /// </para>
    /// <para>
    /// One question and one value, never a list of either. A list is the first
    /// step of the query builder the three shapes above exist to avoid, and
    /// the segment that wants two answers is two campaigns — which is also the
    /// honest answer, because two answers mailed together cannot be reported
    /// on separately afterwards.
    /// </para>
    /// </remarks>
    public sealed record FormAnswer(Guid FormId, string Question, string Value) : Segment
    {
        public override string Type => "formAnswer";
    }

    /// <summary>
    /// One recipient on a typed list: an address, and a name if we were given
    /// one.
    /// </summary>
    /// <remarks>
    /// Both names are optional and frequently absent, which is the whole
    /// reason this record exists rather than a wider required shape. The two
    /// things a list of addresses is for pull in opposite directions: four
    /// sponsors typed in by hand have no names on file anywhere, and four
    /// hundred students lifted out of last year's spreadsheet do.
    /// <para>
    /// Nothing here refuses a missing name. What refuses is the coverage check
    /// in <c>CampaignEndpoints</c>, at send, and only when the template
    /// actually uses the placeholder — so a greeting template aimed at a list
    /// with a blank in it is stopped, and a template that greets nobody still
    /// reaches the sponsors. Requiring names here instead would mean the
    /// sponsor case could no longer be expressed at all.
    /// </para>
    /// </remarks>
    public sealed record AddressRecipient(string Email, string? FirstName, string? LastName);

    /// <summary>
    /// These addresses and no others.
    /// </summary>
    /// <remarks>
    /// The escape hatch, and the reason the other two do not need to grow. A
    /// mentor, four sponsors and somebody's supervisor are not a filter over
    /// <c>applications.*</c> and never will be.
    /// <para>
    /// These resolve with no person id, because an address here is frequently
    /// somebody who has no row in this system at all. That is exactly why
    /// 0015 added a unique index on (campaign_id, to_email): the person-based
    /// one from 0003 does not stop a duplicate when the person is unknown.
    /// </para>
    /// <para>
    /// Names ride along because the list is as often an import as it is four
    /// addresses typed in. Carrying them here rather than looking them up is
    /// the only option available: these people have no row to look anything up
    /// in, which is what makes this segment the one they can be reached by.
    /// </para>
    /// </remarks>
    public sealed record Addresses(IReadOnlyList<AddressRecipient> Recipients) : Segment
    {
        public override string Type => "explicitList";
    }

    /// <summary>
    /// The most a segment may resolve to.
    /// </summary>
    /// <remarks>
    /// Not a rate limit — lark paces the actual sending at roughly fourteen a
    /// second and drains ten thousand rows in under half an hour, so the queue
    /// is not the thing at risk. This is a sanity bound on a number nobody
    /// intended. This event has several hundred applicants; a segment that
    /// resolves to five figures is a mistake somebody is about to make
    /// irreversibly, and the cheapest place to catch it is before the rows
    /// exist.
    /// </remarks>
    public const int MaxRecipients = 10_000;

    /// <summary>
    /// The document that gets stored.
    /// </summary>
    /// <remarks>
    /// Written from the parsed segment rather than from the JSON that arrived,
    /// so what is on the row is what the server understood — not what somebody
    /// sent plus whatever extra properties rode along unread. A stored segment
    /// that includes a field nothing acts on is a stored segment that lies to
    /// the next person who reads it.
    /// <para>
    /// Statuses go in as their stored spellings rather than enum names, so the
    /// document can be read against <c>applications.applications.status</c>
    /// with no lookup table and survives a C# member being renamed.
    /// </para>
    /// </remarks>
    public string ToJson() => this switch
    {
        InStatus s => JsonSerializer.Serialize(new
        {
            type = s.Type,
            eventId = s.EventId,
            statuses = s.Statuses.Select(x => x.ToWire()),
        }),
        FormRespondents s => JsonSerializer.Serialize(new { type = s.Type, formId = s.FormId }),
        FormAnswer s => JsonSerializer.Serialize(new
        {
            type = s.Type,
            formId = s.FormId,
            question = s.Question,
            value = s.Value,
        }),
        // Written as `recipients` rather than the `emails` this used to store,
        // because the elements are no longer addresses. Documents written
        // before this are still read -- see TryAddresses -- so the rename
        // costs nothing and the stored shape describes itself.
        Addresses s => JsonSerializer.Serialize(new
        {
            type = s.Type,
            recipients = s.Recipients.Select(r => new
            {
                email = r.Email,
                firstName = r.FirstName,
                lastName = r.LastName,
            }),
        }),
        _ => throw new InvalidOperationException($"No stored shape for '{Type}'."),
    };

    /// <summary>
    /// Reads a segment out of the request, or says what is wrong with it.
    /// </summary>
    /// <remarks>
    /// Every failure returns a sentence somebody can act on. The alternative
    /// is a deserialization exception surfacing as a 400 with a type name in
    /// it, on the screen where somebody is about to mail four hundred people
    /// — the one screen where being told what is wrong actually matters.
    /// </remarks>
    public static bool TryParse(JsonElement json, out Segment? segment, out string? error)
    {
        segment = null;
        error = null;

        if (json.ValueKind != JsonValueKind.Object)
        {
            error = "A segment is an object saying who to send to.";
            return false;
        }

        if (!json.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            error = "A segment needs a type: applicationStatus, formRespondents, "
                    + "formAnswer or explicitList.";
            return false;
        }

        switch (type.GetString())
        {
            case "applicationStatus":
                return TryStatuses(json, out segment, out error);
            case "formRespondents":
                if (!TryId(json, "formId", out var formId))
                {
                    error = "This segment needs the form its recipients answered.";
                    return false;
                }

                segment = new FormRespondents(formId);
                return true;
            case "formAnswer":
                return TryAnswer(json, out segment, out error);
            case "explicitList":
                return TryAddresses(json, out segment, out error);
            default:
                error = "That is not a segment we know how to send to. Pick "
                        + "applicationStatus, formRespondents, formAnswer or "
                        + "explicitList.";
                return false;
        }
    }

    /// <summary>
    /// The longest question key and answer value this will accept.
    /// </summary>
    /// <remarks>
    /// Both are bounds on text that ends up in a stored jsonb document and in
    /// a bound parameter, so neither is a style rule. The key bound is
    /// <c>DraftKeys</c>'s own shape — a key is at most 63 characters, because
    /// it eventually has to be a column name — and the value bound is the cap
    /// <c>SubmissionValidation</c> puts on a short answer, so a value this
    /// refuses is one no answer could have been.
    /// </remarks>
    private const int MaxQuestionLength = 63;

    private const int MaxValueLength = 500;

    /// <summary>
    /// One question, one value, and a sentence for each way that can be wrong.
    /// </summary>
    /// <remarks>
    /// The value is trimmed and nothing else is done to it. Lower-casing it
    /// here would be a second place that decides how a value is compared —
    /// <see cref="PostgresSegmentResolver"/> is the first — and a stored
    /// segment saying <c>hardware</c> when the organizer picked
    /// <c>Hardware</c> is a stored segment that does not say what was chosen.
    /// </remarks>
    private static bool TryAnswer(JsonElement json, out Segment? segment, out string? error)
    {
        segment = null;
        error = null;

        if (!TryId(json, "formId", out var formId))
        {
            error = "This segment needs the form the answer was given on.";
            return false;
        }

        var question = Trimmed(json, "question");
        if (question is null)
        {
            error = "Choose which question's answer this segment means.";
            return false;
        }

        if (question.Length > MaxQuestionLength)
        {
            error = "That is not a question key this form could have.";
            return false;
        }

        var value = Trimmed(json, "value");
        if (value is null)
        {
            // Not the same as "has not answered". A segment with no value
            // would mean "answered this question with anything", which is a
            // different and much larger audience than anybody picking a value
            // intends — so it is refused rather than guessed at.
            error = "Choose which answer to that question this segment means.";
            return false;
        }

        if (value.Length > MaxValueLength)
        {
            error = string.Format(
                CultureInfo.InvariantCulture,
                "That answer is longer than the {0:N0} characters a short answer can be.",
                MaxValueLength);
            return false;
        }

        segment = new FormAnswer(formId, question, value);
        return true;
    }

    private static bool TryStatuses(JsonElement json, out Segment? segment, out string? error)
    {
        segment = null;
        error = null;

        if (!TryId(json, "eventId", out var eventId))
        {
            error = "This segment needs the event whose applicants it means.";
            return false;
        }

        if (!json.TryGetProperty("statuses", out var listed)
            || listed.ValueKind != JsonValueKind.Array
            || listed.GetArrayLength() == 0)
        {
            error = "Choose at least one application status to send to.";
            return false;
        }

        var statuses = new List<ApplicationStatus>();
        foreach (var element in listed.EnumerateArray())
        {
            ApplicationStatus parsed;
            try
            {
                parsed = ApplicationStatuses.Parse(element.GetString() ?? string.Empty);
            }
            catch (ArgumentException)
            {
                // Named back, because the list came off a set of checkboxes and
                // the one that is wrong is the only useful thing to say.
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}' is not an application status.",
                    element.ValueKind == JsonValueKind.String ? element.GetString() : "that");
                return false;
            }

            if (!statuses.Contains(parsed))
            {
                statuses.Add(parsed);
            }
        }

        segment = new InStatus(eventId, statuses);
        return true;
    }

    /// <summary>
    /// The longest name this will carry into a message.
    /// </summary>
    /// <remarks>
    /// A bound on text that ends up in a stored jsonb document and then,
    /// rendered, in <c>notify.messages</c>. A spreadsheet column with a
    /// paragraph in it is a mis-mapped column rather than somebody's name, and
    /// the cheapest place to say so is before several hundred of them are
    /// stored.
    /// </remarks>
    private const int MaxNameLength = 100;

    /// <summary>
    /// A typed list of recipients, in either shape it has ever been stored in.
    /// </summary>
    /// <remarks>
    /// Two shapes on purpose. <c>recipients</c> is what this writes now: an
    /// object per person carrying an address and optionally a name.
    /// <c>emails</c> is what it wrote before names existed, a plain array of
    /// strings, and every campaign drafted until then has one sitting in
    /// <c>notify.campaigns.segment</c> — a document whose whole purpose is to
    /// still be readable a month later. Dropping that branch would make those
    /// campaigns unreadable rather than merely old.
    /// <para>
    /// A string inside <c>recipients</c> is read as an address with no names,
    /// so a caller that has only addresses does not have to wrap each one in
    /// an object to say nothing.
    /// </para>
    /// <para>
    /// A blank name is stored as absent rather than as an empty string. They
    /// mean the same thing to everything downstream, and one spelling of
    /// nothing is what keeps the coverage check's count honest.
    /// </para>
    /// </remarks>
    private static bool TryAddresses(JsonElement json, out Segment? segment, out string? error)
    {
        segment = null;
        error = null;

        if (!json.TryGetProperty("recipients", out var listed)
            || listed.ValueKind != JsonValueKind.Array)
        {
            if (!json.TryGetProperty("emails", out listed)
                || listed.ValueKind != JsonValueKind.Array)
            {
                error = "Add at least one address to send to.";
                return false;
            }
        }

        if (listed.GetArrayLength() == 0)
        {
            error = "Add at least one address to send to.";
            return false;
        }

        if (listed.GetArrayLength() > MaxRecipients)
        {
            error = string.Format(
                CultureInfo.InvariantCulture,
                "That is more than {0:N0} addresses, which is more than this is for.",
                MaxRecipients);
            return false;
        }

        // Deduplicated as it is read, and case-insensitively, because these
        // are pasted out of a spreadsheet. Two spellings of one address in the
        // list would be caught by the unique index at send anyway; catching it
        // here is what makes the number on the preview screen the truth.
        //
        // First spelling wins, names and all. A second row for the same person
        // is a duplicate rather than a correction, and there is nothing here
        // that could tell which of the two was meant.
        var recipients = new List<AddressRecipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var element in listed.EnumerateArray())
        {
            string? email;
            string? firstName = null;
            string? lastName = null;

            if (element.ValueKind == JsonValueKind.String)
            {
                email = element.GetString()?.Trim();
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                email = Trimmed(element, "email");
                firstName = Trimmed(element, "firstName");
                lastName = Trimmed(element, "lastName");
            }
            else
            {
                error = "One of those recipients is not an address.";
                return false;
            }

            if (string.IsNullOrEmpty(email))
            {
                error = "One of those addresses is blank.";
                return false;
            }

            if (firstName?.Length > MaxNameLength || lastName?.Length > MaxNameLength)
            {
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    "A name longer than {0:N0} characters came through for {1}, "
                    + "which usually means a column was mapped to the wrong thing.",
                    MaxNameLength,
                    email);
                return false;
            }

            if (seen.Add(email))
            {
                recipients.Add(new AddressRecipient(email, firstName, lastName));
            }
        }

        segment = new Addresses(recipients);
        return true;
    }

    /// <summary>One required string property, trimmed, or null for nothing.</summary>
    /// <remarks>
    /// Blank and absent are the same answer here. A question key of three
    /// spaces is not a question somebody chose, and treating the two
    /// differently would mean two sentences for one mistake.
    /// </remarks>
    private static string? Trimmed(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : null;

    private static bool TryId(JsonElement json, string name, out Guid id)
    {
        id = Guid.Empty;
        return json.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.String
               && Guid.TryParse(value.GetString(), out id);
    }
}
