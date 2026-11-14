using System.Globalization;
using System.Text.Json;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Forms;
using Npgsql;
using NpgsqlTypes;

namespace MorganHacks.Applications.Segments;

/// <summary>
/// Resolves segments against <c>applications.*</c>.
/// </summary>
/// <remarks>
/// Every query here reads one more row than it is allowed to return, so the
/// caller can tell "exactly the maximum" from "more than the maximum" without
/// counting the whole table first. See <see cref="Segment.MaxRecipients"/> for
/// why there is a maximum at all.
/// </remarks>
public sealed class PostgresSegmentResolver(NpgsqlDataSource dataSource) : ISegmentResolver
{
    /// <summary>
    /// The select list every applicant query shares.
    /// </summary>
    /// <remarks>
    /// Built from <see cref="ApplicantColumns.Mergeable"/> rather than typed
    /// out, so a column becomes readable here by being declared there and by
    /// nothing else. The two queries below would otherwise be a second and a
    /// third place to remember, and the failure of forgetting one is an editor
    /// offering a placeholder that comes out blank for a whole segment.
    /// <para>
    /// Interpolated into SQL, which is worth being explicit about: every part
    /// of this string is a column name from a list declared in code, and a
    /// test asserts that every one of them is a real column of
    /// <c>applications.applications</c>. Nothing a caller sends reaches it.
    /// </para>
    /// <para>
    /// Withheld columns are not selected at all. A resume key that is never
    /// read out of the table cannot end up in a rendered body by accident.
    /// </para>
    /// </remarks>
    private static readonly string Selected = string.Join(
        ", ",
        new[] { "a.person_id" }.Concat(
            ApplicantColumns.Mergeable.Select(column => $"a.{column.Column}")));

    public Task<ResolvedSegment> ResolveAsync(
        Segment segment, CancellationToken ct = default) => segment switch
        {
            Segment.InStatus s => InStatusAsync(s, ct),
            Segment.FormRespondents s => RespondentsAsync(s, ct),
            Segment.FormAnswer s => AnsweredAsync(s, ct),
            Segment.Addresses s => Task.FromResult(Addresses(s)),
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment, null),
        };

    /// <summary>
    /// One application, read exactly as a segment reads it.
    /// </summary>
    /// <remarks>
    /// The same select list and the same reader as everything else here, which
    /// is the only reason this method is allowed to exist — see
    /// <see cref="ISegmentResolver.MemberOfAsync"/> for why a second place
    /// building a <see cref="SegmentMember"/> would be a second list of
    /// columns to keep in step.
    /// <para>
    /// No limit and no overflow check. One id is one row by primary key, so
    /// the reasoning <see cref="Segment.MaxRecipients"/> exists for has
    /// nothing to apply to.
    /// </para>
    /// </remarks>
    public async Task<SegmentMember?> MemberOfAsync(
        Guid applicationId, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
             WHERE a.id = @applicationId
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("applicationId", applicationId);

        var resolved = await ReadAsync(cmd, ct);
        return resolved.Members.Count == 1 ? resolved.Members[0] : null;
    }

    /// <summary>
    /// Everyone on one event whose application is in one of these states.
    /// </summary>
    /// <remarks>
    /// No <c>DISTINCT</c> and no dedupe pass, because the unique index on
    /// (event_id, lower(email)) already guarantees one row per address per
    /// event. That index is the reason one person cannot appear twice in a
    /// decision email, and it holds no matter what wrote the rows.
    /// </remarks>
    private async Task<ResolvedSegment> InStatusAsync(
        Segment.InStatus segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
             WHERE a.event_id = @eventId AND a.status = ANY(@statuses)
             ORDER BY a.email
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", segment.EventId);
        cmd.Parameters.Add(new NpgsqlParameter("statuses", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = segment.Statuses.Select(s => s.ToWire()).ToArray(),
        });
        cmd.Parameters.AddWithValue("limit", Segment.MaxRecipients + 1);

        return await ReadAsync(cmd, ct);
    }

    /// <summary>
    /// Everyone who submitted a given form.
    /// </summary>
    /// <remarks>
    /// Reached through the form's event, because an application carries a form
    /// version and not a form id. The <c>kind = 'application'</c> check is what
    /// keeps that honest: a survey sitting on the same event would otherwise
    /// resolve to the application form's respondents, and somebody would mail
    /// four hundred applicants a note meant for eleven mentors.
    /// <para>
    /// <c>submitted_at IS NOT NULL</c> rather than a status list. Somebody who
    /// opened the form and typed their name has a row — the form autosaves —
    /// and they have not answered anything.
    /// </para>
    /// </remarks>
    private async Task<ResolvedSegment> RespondentsAsync(
        Segment.FormRespondents segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
              JOIN applications.forms f ON f.event_id = a.event_id
             WHERE f.id = @formId
               AND f.kind = 'application'
               AND a.submitted_at IS NOT NULL
             ORDER BY a.email
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("formId", segment.FormId);
        cmd.Parameters.AddWithValue("limit", Segment.MaxRecipients + 1);

        return await ReadAsync(cmd, ct);
    }

    /// <summary>
    /// The jsonb predicate that decides whether one answer matches.
    /// </summary>
    /// <remarks>
    /// <b>Equality and membership, and nothing else.</b> The four alternatives
    /// are one rule read against the four shapes an answer is actually stored
    /// in, not four features:
    /// <list type="bullet">
    /// <item><c>@&gt; @exact</c> — the answer <em>is</em> this value. A choice,
    /// a short answer, a date.</item>
    /// <item><c>@&gt; @chosen</c> — this value is <em>one of</em> those
    /// selected. jsonb containment on an array means "contains this element",
    /// which is exactly what a checkbox question asks.</item>
    /// <item><c>@&gt; @alternate</c> — the same value as a JSON boolean or
    /// number. This is not laxity: <c>applications.responses</c> is written
    /// through a normaliser that types the value, and
    /// <c>form_submissions.answers</c> holds what the browser posted, where a
    /// number input posts <c>"21"</c> and a checkbox posts <c>true</c>. One
    /// stored answer in two encodings is one answer, and a segment that
    /// matched only one of them would quietly halve the audience.</item>
    /// <item><c>lower(-&gt;&gt;) = lower()</c> — the short-answer case, which
    /// is the only one that is case-insensitive. Somebody typing
    /// <c>Hardware</c> and somebody typing <c>hardware</c> gave the same
    /// answer, and an organizer picking a value cannot know which they typed.
    /// </item>
    /// </list>
    /// <para>
    /// <b>Long free text is deliberately not here.</b> There is no
    /// <c>LIKE '%...%'</c> branch, and the reason is not that it is hard: it
    /// is that substring matching over a paragraph is a search feature wearing
    /// a segment's clothes. "Everyone who typed <em>hardware</em> somewhere in
    /// a paragraph" is a far vaguer audience than it sounds, it cannot be read
    /// back as a sentence a month later, and it cannot use either GIN index —
    /// <c>jsonb_ops</c> indexes whole values, so a containment lookup is an
    /// index scan and a substring test is a recheck of every row. A paragraph
    /// question can still be targeted by this, but only by typing the answer
    /// back verbatim, which nobody will do by accident. The picker does not
    /// offer them — see <see cref="AnswerQuestions"/>, which says so in a
    /// sentence an organizer reads.
    /// </para>
    /// <para>
    /// The column name is interpolated and the question and value are bound.
    /// Every call site is in this file and passes a literal; nothing a caller
    /// of the resolver sends reaches the string.
    /// </para>
    /// </remarks>
    private static string Matches(string column) => $"""
        (    {column} @> @exact
          OR {column} @> @chosen
          OR (@alternate IS NOT NULL AND {column} @> @alternate)
          OR lower({column} ->> @question) = lower(@value))
        """;

    /// <summary>
    /// Everyone who answered one question on one form with one value.
    /// </summary>
    /// <remarks>
    /// Two places an answer can live, so two branches over one select list.
    /// An application form's answers are in
    /// <c>applications.applications.responses</c>, reached through the form's
    /// event because an application carries a form version and not a form id —
    /// the same reasoning <see cref="RespondentsAsync"/> gives. Every other
    /// kind of form writes <c>applications.form_submissions.answers</c>, and
    /// those are reached back to an applicant through the person who gave
    /// them.
    /// <para>
    /// A form has one kind, so only one branch can match — but the branches
    /// are an <c>OR</c> over one row of <c>applications</c> rather than a
    /// union, so a row that somehow satisfied both is still one recipient.
    /// <c>a.event_id = f.event_id</c> is what makes that true across events as
    /// well: without it, somebody who has applied two years running would
    /// resolve twice from one answer.
    /// </para>
    /// <para>
    /// <b>Anonymous answers are excluded, by name.</b>
    /// <c>0027_anonymous_form_answers.sql</c> made <c>person_id</c> nullable so
    /// an answer with nobody attached could be kept, and those rows have no
    /// person and therefore no address. <c>s.person_id IS NOT NULL</c> is
    /// implied by the join below and written anyway, because it is the rule
    /// rather than a consequence of how the join happens to be spelled today.
    /// They are counted instead — see <see cref="UnreachableAsync"/> — because
    /// a recipient count that is quietly smaller than the response count on
    /// the form screen reads as a bug.
    /// </para>
    /// </remarks>
    private async Task<ResolvedSegment> AnsweredAsync(
        Segment.FormAnswer segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT {Selected}
              FROM applications.applications a
              JOIN applications.forms f ON f.id = @formId
             WHERE a.event_id = f.event_id
               AND ((f.kind = 'application'
                     AND a.submitted_at IS NOT NULL
                     AND {Matches("a.responses")})
                 OR EXISTS (SELECT 1
                              FROM applications.form_submissions s
                             WHERE s.form_id = f.id
                               AND s.person_id IS NOT NULL
                               AND s.person_id = a.person_id
                               AND {Matches("s.answers")}))
             ORDER BY a.email
             LIMIT @limit
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        Criterion(cmd, segment);
        cmd.Parameters.AddWithValue("limit", Segment.MaxRecipients + 1);

        var resolved = await ReadAsync(cmd, ct);
        if (resolved.Overflowed)
        {
            // Nothing is being shown, so there is nothing to explain. The
            // caller refuses on this before it reads a count.
            return resolved;
        }

        return resolved with { Unreachable = await UnreachableAsync(segment, ct) };
    }

    /// <summary>
    /// How many matching answers there is nobody to mail about.
    /// </summary>
    /// <remarks>
    /// Every answer that matched and has no applicant behind it on the form's
    /// event: an anonymous submission, or one from somebody who signed in to a
    /// survey without ever applying. Both are real and both are invisible to
    /// the query above, and the number exists so the screen can say why forty
    /// responses are thirty-one recipients instead of leaving an organizer to
    /// guess.
    /// <para>
    /// A second round trip rather than a window function on the first. The two
    /// questions are counted over different tables — people, then answers —
    /// and folding them together would mean the recipient query carried a
    /// subquery that does not affect which rows it returns, which is the kind
    /// of thing that gets deleted by somebody reading it as dead weight.
    /// </para>
    /// <para>
    /// Zero for an application form, with no branch needed: its answers live
    /// on the application, so it has no <c>form_submissions</c> rows for this
    /// to count.
    /// </para>
    /// </remarks>
    private async Task<int> UnreachableAsync(Segment.FormAnswer segment, CancellationToken ct)
    {
        var sql = $"""
            SELECT count(*)
              FROM applications.form_submissions s
              JOIN applications.forms f ON f.id = s.form_id
             WHERE s.form_id = @formId
               AND {Matches("s.answers")}
               AND NOT EXISTS (SELECT 1
                                 FROM applications.applications a
                                WHERE a.event_id = f.event_id
                                  AND a.person_id = s.person_id)
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        Criterion(cmd, segment);

        return (int)(long)(await cmd.ExecuteScalarAsync(ct))!;
    }

    // ------------------------------------------------------------ merging ---

    /// <summary>
    /// What these recipients answered one form, in one query.
    /// </summary>
    /// <remarks>
    /// The same two-table split <see cref="AnsweredAsync"/> matches against,
    /// read rather than filtered: an application form's answers are in
    /// <c>applications.applications.responses</c> and every other kind's are in
    /// <c>applications.form_submissions.answers</c>. A form has exactly one
    /// kind, so this is one statement and not a union — which is the whole
    /// performance story. Rendering a campaign calls this once, before the loop
    /// over recipients, so merging an answer costs one query however many
    /// people are in the send. The obvious version — look the answer up inside
    /// the render loop — is four hundred round trips behind one button, and it
    /// would be four hundred fast ones, which is how it would survive review.
    /// <para>
    /// Both branches key on what <see cref="SegmentMember"/> actually carries.
    /// The application branch keys on the address, because
    /// <c>applications.person_id</c> is null for everybody who applied without
    /// signing in and the answers hang off the application rather than off a
    /// person. The submission branch has to key on the person — that is the
    /// only thing <c>form_submissions</c> holds — and the address is put back
    /// on from the members themselves rather than by joining
    /// <c>applications</c> a second time. One lookup, one key, and no join
    /// whose only job is to turn an id back into something we were already
    /// handed.
    /// </para>
    /// <para>
    /// <c>submitted_at IS NOT NULL</c> on the application branch, for the
    /// reason <see cref="RespondentsAsync"/> gives: the form autosaves, so
    /// somebody who opened it and typed half an answer already has a row. An
    /// email quoting a sentence they abandoned is worse than one that leaves
    /// the blank standing for the coverage check to refuse.
    /// </para>
    /// <para>
    /// Nothing is logged here and nothing is returned beyond the questions that
    /// were asked for. The rest of the jsonb is read out of the row and
    /// dropped — a key-by-key projection in SQL would mean a lateral join per
    /// row to answer what a dictionary lookup answers, and what matters is that
    /// an unoffered answer is never written anywhere, which is what the filter
    /// guarantees.
    /// </para>
    /// </remarks>
    public async Task<FormAnswers> AnswersToAsync(
        Form form,
        IReadOnlyList<AnswerQuestion> questions,
        IReadOnlyList<SegmentMember> members,
        CancellationToken ct = default)
    {
        // No form questions, or nobody to look them up for. Both are ordinary:
        // an unpublished form asks nothing, and the placeholder endpoints want
        // the question list without resolving a segment at all. Neither is a
        // reason to open a connection.
        if (questions.Count == 0 || members.Count == 0)
        {
            return FormAnswers.Asked(questions);
        }

        var wanted = new HashSet<string>(
            questions.Select(question => question.Key), StringComparer.Ordinal);

        return new FormAnswers(
            questions,
            form.IsApplication
                ? await RespondedAsync(form, members, wanted, ct)
                : await SubmittedAsync(form, members, wanted, ct));
    }

    /// <summary>
    /// An application form's answers, which are on the application itself.
    /// </summary>
    /// <remarks>
    /// Reached through the form's event rather than through a form id, because
    /// an application carries a form version and not a form — the same
    /// reasoning <see cref="RespondentsAsync"/> and <see cref="AnsweredAsync"/>
    /// both give. The consequence is worth naming: a campaign aimed at last
    /// season's applicants whose template names this season's form resolves no
    /// answers at all, every placeholder stands, and the send is refused. That
    /// is the right failure rather than a missing feature — those people
    /// answered a different form.
    /// <para>
    /// <c>lower(email)</c> on both sides, which is what
    /// <c>applications_event_email_key</c> is built on, so this is an index
    /// lookup rather than a scan of the event's applicants.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>>
        RespondedAsync(
            Form form,
            IReadOnlyList<SegmentMember> members,
            IReadOnlySet<string> wanted,
            CancellationToken ct)
    {
        const string sql = """
            SELECT a.email, a.responses
              FROM applications.applications a
             WHERE a.event_id = @eventId
               AND a.submitted_at IS NOT NULL
               AND lower(a.email) = ANY(@emails)
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("eventId", form.EventId);
        cmd.Parameters.Add(new NpgsqlParameter("emails", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = members
                .Select(member => member.Email.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
        });

        var given = FormAnswers.EmptyGiven();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            given[reader.GetString(0)] = Kept(reader.GetString(1), wanted);
        }

        return given;
    }

    /// <summary>
    /// Every other kind of form's answers, which are in
    /// <c>form_submissions</c>.
    /// </summary>
    /// <remarks>
    /// Keyed on the person, which is the only thing that table holds, and
    /// turned back into an address from the members themselves. Anybody with no
    /// person id is skipped and has nothing: a typed list of addresses is
    /// mentors and sponsors this system has never heard of, and an answer
    /// belongs to somebody who signed in to give it.
    /// <para>
    /// <c>form_submissions_form_person_key</c> covers the lookup and also means
    /// a person cannot have two rows for one form, so no row here can be
    /// overwritten by a later one.
    /// </para>
    /// <para>
    /// Anonymous answers are excluded by having no person to match, which is
    /// the same outcome <see cref="AnsweredAsync"/> states in as many words.
    /// They are not counted here, unlike there: an answer with nobody behind it
    /// has no address to merge into and nothing to say about a send's coverage.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>>
        SubmittedAsync(
            Form form,
            IReadOnlyList<SegmentMember> members,
            IReadOnlySet<string> wanted,
            CancellationToken ct)
    {
        var addressed = new Dictionary<Guid, List<string>>();

        foreach (var member in members)
        {
            if (member.PersonId is not { } person)
            {
                continue;
            }

            // A list rather than one address, because nothing forbids two. The
            // dedupe index is on (event_id, lower(email)) and not on the
            // person, so somebody who applied twice under two addresses is one
            // person and two recipients -- and both of them answered the
            // survey, because the answer is filed against the person. Keeping
            // whichever arrived last would silently drop a message.
            if (!addressed.TryGetValue(person, out var emails))
            {
                addressed[person] = emails = [];
            }

            emails.Add(member.Email);
        }

        var given = FormAnswers.EmptyGiven();

        if (addressed.Count == 0)
        {
            return given;
        }

        const string sql = """
            SELECT s.person_id, s.answers
              FROM applications.form_submissions s
             WHERE s.form_id = @formId
               AND s.person_id = ANY(@people)
            """;

        await using var cmd = dataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("formId", form.Id);
        cmd.Parameters.Add(new NpgsqlParameter("people", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
        {
            Value = addressed.Keys.ToArray(),
        });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var answers = Kept(reader.GetString(1), wanted);

            foreach (var email in addressed[reader.GetGuid(0)])
            {
                given[email] = answers;
            }
        }

        return given;
    }

    /// <summary>
    /// One row's answers, narrowed to the questions that were asked for.
    /// </summary>
    /// <remarks>
    /// Cloned, because each element is a window onto a document this method is
    /// about to dispose — the same reason <c>PostgresResponseStore.Read</c>
    /// clones, and the same bug if it is forgotten: the values survive as
    /// garbage rather than as an exception.
    /// <para>
    /// Anything not asked for is dropped here and never leaves this method. A
    /// question an author added after the template was written, and a file
    /// upload's storage id, are both in this jsonb and neither is something a
    /// message may carry.
    /// </para>
    /// </remarks>
    private static IReadOnlyDictionary<string, JsonElement> Kept(
        string json, IReadOnlySet<string> wanted)
    {
        var kept = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            // NOT NULL DEFAULT '{}' on both columns, so this is unreachable
            // unless something wrote an array or a scalar into one. Answered
            // with nothing rather than a throw: the caller is halfway through
            // preparing a send, and one unreadable row is a standing
            // placeholder and a refusal rather than a 500.
            return kept;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (wanted.Contains(property.Name))
            {
                kept[property.Name] = property.Value.Clone();
            }
        }

        return kept;
    }

    /// <summary>
    /// Every parameter both statements read, from one criterion.
    /// </summary>
    /// <remarks>
    /// The documents are built here rather than with
    /// <c>jsonb_build_object</c> in the statement, so what reaches
    /// <c>@&gt;</c> is a plain parameter. A GIN index can serve a containment
    /// test against a parameter; whether it can serve one against a function
    /// call depends on how that function is declared, and an index this
    /// feature added a migration for should not be optional on a detail like
    /// that.
    /// <para>
    /// <see cref="JsonSerializer"/> rather than string concatenation, because
    /// a question key is a form author's text and a value can contain a quote.
    /// </para>
    /// </remarks>
    private static void Criterion(NpgsqlCommand cmd, Segment.FormAnswer segment)
    {
        cmd.Parameters.AddWithValue("formId", segment.FormId);

        // Typed rather than inferred, because both are operands of an
        // overloaded thing: lower() also takes a range, and -> takes an
        // integer as well as a key. A parameter that arrives without a type
        // makes those ambiguous rather than wrong, which fails at the
        // statement and not at the value.
        cmd.Parameters.Add(new NpgsqlParameter("question", NpgsqlDbType.Text)
        {
            Value = segment.Question,
        });
        cmd.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Text)
        {
            Value = segment.Value,
        });

        Json(cmd, "exact", Document(segment.Question, segment.Value));
        Json(cmd, "chosen", Document(segment.Question, new[] { segment.Value }));
        Json(cmd, "alternate", Alternate(segment.Question, segment.Value));
    }

    /// <summary>The same value as a JSON boolean or number, where it is one.</summary>
    /// <remarks>
    /// Null when the value is neither, which is the common case: a choice is a
    /// string in both tables and needs no second encoding. The parse is
    /// invariant-culture on purpose — the value came out of a form's option
    /// list or a number input, neither of which is localised.
    /// </remarks>
    private static string? Alternate(string question, string value)
    {
        if (bool.TryParse(value, out var yes))
        {
            return Document(question, yes);
        }

        return decimal.TryParse(
            value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? Document(question, number)
            : null;
    }

    private static string Document(string question, object? value) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>(StringComparer.Ordinal) { [question] = value });

    private static void Json(NpgsqlCommand cmd, string name, string? document) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Jsonb)
        {
            Value = (object?)document ?? DBNull.Value,
        });

    /// <summary>
    /// The addresses somebody typed, and nothing looked up about them.
    /// </summary>
    /// <remarks>
    /// No database round trip on purpose. Matching these against
    /// <c>identity.people</c> to fill in a person id would mean reading
    /// another module's table, and matching them against applicants would
    /// defeat the point — this segment exists for the people who are not in
    /// the applicant pool.
    /// <para>
    /// The consequence is that these messages carry no person id, so they do
    /// not appear in the "what have we sent you" history that is keyed on one.
    /// That is the correct answer for a sponsor contact and a small loss for
    /// an applicant somebody happened to paste in.
    /// </para>
    /// <para>
    /// An address fills in the one column
    /// <see cref="ApplicantColumn.OnAddressLists"/> marks and no other, so a
    /// template greeting a sponsor by name is refused before the send rather
    /// than mailed with the greeting still standing in it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A typed list, which needs no query: the segment already is the answer.
    /// </summary>
    /// <remarks>
    /// Null person ids throughout, because these people frequently have no row
    /// in this system — which is the point of the shape.
    /// <para>
    /// A name is written into <see cref="SegmentMember.Fields"/> only when
    /// there is one, never as an empty string. That is what lets the coverage
    /// check count the blanks: a key present with nothing behind it reads as
    /// filled, and a greeting template would go out to somebody as "Hi ,"
    /// having passed every check on the way.
    /// </para>
    /// </remarks>
    private static ResolvedSegment Addresses(Segment.Addresses segment)
    {
        var members = new List<SegmentMember>(segment.Recipients.Count);

        foreach (var recipient in segment.Recipients)
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ApplicantColumns.Address.Column] = recipient.Email,
            };

            if (recipient.FirstName is { Length: > 0 } first)
            {
                fields[ApplicantColumns.FirstName.Column] = first;
            }

            if (recipient.LastName is { Length: > 0 } last)
            {
                fields[ApplicantColumns.LastName.Column] = last;
            }

            members.Add(new SegmentMember(null, recipient.Email, fields));
        }

        return new ResolvedSegment(members, Overflowed: false);
    }

    /// <summary>
    /// Reads the rows into members, one column of
    /// <see cref="ApplicantColumns.Mergeable"/> at a time.
    /// </summary>
    /// <remarks>
    /// By ordinal rather than by name, because <see cref="Selected"/> put them
    /// in this order and reading them back by name would be a second agreement
    /// to keep. The value is taken as the column holds it — string, int or
    /// bool — and how each of those reads in a sentence is decided in the mail
    /// rather than here.
    /// </remarks>
    private static async Task<ResolvedSegment> ReadAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var members = new List<SegmentMember>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < ApplicantColumns.Mergeable.Count; i++)
            {
                // Offset by one: person_id is the first thing Selected asks for.
                var ordinal = i + 1;
                fields[ApplicantColumns.Mergeable[i].Column] =
                    await reader.IsDBNullAsync(ordinal, ct) ? null : reader.GetValue(ordinal);
            }

            members.Add(new SegmentMember(
                await reader.IsDBNullAsync(0, ct) ? null : reader.GetGuid(0),

                // NOT NULL on the column, so this cast cannot be the thing that
                // fails; the divergence test is what keeps the column there.
                (string)fields[ApplicantColumns.Address.Column]!,
                fields));
        }

        if (members.Count > Segment.MaxRecipients)
        {
            return new ResolvedSegment([], Overflowed: true);
        }

        return new ResolvedSegment(members, Overflowed: false);
    }
}
