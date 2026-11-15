using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MorganHacks.Applications.Domain;
using MorganHacks.Applications.Segments;
using MorganHacks.Applications.Services;
using MorganHacks.Lark.Data.Data;

// Just the types, not the namespace, for the reason MergeFields itself gives:
// Forms and Segments each declare a ColumnKind, and MergeFieldSchemaTests below
// reads the Segments one.
using AnswerStorage = MorganHacks.Applications.Forms.AnswerStorage;
using FieldType = MorganHacks.Applications.Forms.FieldType;
using Form = MorganHacks.Applications.Forms.Form;
using FormField = MorganHacks.Applications.Forms.FormField;
using FormVersion = MorganHacks.Applications.Forms.FormVersion;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The placeholder catalogue against the table it claims to describe.
/// </summary>
/// <remarks>
/// This is the test that lets <see cref="ApplicantColumns"/> be a list in code
/// rather than a read of <c>information_schema</c> on every request. The
/// declaration is the fast, reviewable, decided-once thing; this is what stops
/// it becoming a lie. A column added to <c>applications.applications</c> fails
/// here until somebody says, in a sentence, whether a broadcast may fill
/// itself in from it — which is a build failure with the column named, rather
/// than an API that changed shape because a migration ran.
/// </remarks>
public class MergeFieldSchemaTests(ApplicationsDatabase db)
    : IClassFixture<ApplicationsDatabase>
{
    /// <summary>What Postgres calls the type behind each declared kind.</summary>
    /// <remarks>
    /// <c>information_schema.columns.data_type</c>'s spellings, which is why
    /// <see cref="ColumnKind"/> is named after the SQL type rather than after
    /// how the value reads.
    /// </remarks>
    private static string SqlTypeOf(ColumnKind kind) => kind switch
    {
        ColumnKind.Text => "text",
        ColumnKind.Integer => "integer",
        ColumnKind.Boolean => "boolean",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Every column the real table has, and its type.</summary>
    private async Task<IReadOnlyDictionary<string, string>> ColumnsAsync()
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT column_name, data_type
              FROM information_schema.columns
             WHERE table_schema = 'applications' AND table_name = 'applications'
            """);

        var columns = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }

    [Fact]
    public async Task Every_column_is_either_offered_or_withheld_on_purpose()
    {
        // Both directions, and both matter. A column in the table and not in
        // the catalogue is one nobody decided about, so it is silently
        // unofferable; a column in the catalogue and not in the table is a
        // placeholder an editor offers and a send cannot fill, which is the
        // failure the catalogue exists to remove.
        var actual = (await ColumnsAsync()).Keys;

        Assert.Equal(
            actual.OrderBy(column => column, StringComparer.Ordinal),
            ApplicantColumns.Declared.OrderBy(column => column, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_offered_column_holds_the_type_it_is_declared_to()
    {
        // What makes the per-type rendering safe. MergeFields renders by
        // declared kind and throws if the value is not that type; this is what
        // turns that throw into a build failure instead of a refused send.
        var columns = await ColumnsAsync();

        foreach (var column in ApplicantColumns.Mergeable)
        {
            Assert.Equal(SqlTypeOf(column.Kind), columns[column.Column]);
        }
    }

    [Fact]
    public async Task A_placeholder_reads_a_column_that_is_really_there()
    {
        // The same fact from the placeholder's side, because the name is
        // derived and the derivation is the thing that could be wrong:
        // {{graduationYear}} is only fillable if graduation_year exists.
        var columns = await ColumnsAsync();

        // Only the column-backed ones. A namespaced field like {{link.portal}}
        // has no column to be checked against and is filled from configuration
        // instead, which is what MergeFieldValueTests covers.
        foreach (var field in MergeFields.All.Where(field => field.Column is not null))
        {
            Assert.Contains(field.Column!.Column, columns.Keys);
            Assert.Equal(field.Name, MergeFields.NameFor(field.Column.Column));
        }
    }
}

/// <summary>
/// How each type of value reads once it is in a message.
/// </summary>
/// <remarks>
/// No database. These are about the rendering rather than about the schema,
/// and the schema half is <see cref="MergeFieldSchemaTests"/> next door.
/// </remarks>
public class MergeFieldValueTests
{
    /// <summary>
    /// Configuration with none of the origins set.
    /// </summary>
    /// <remarks>
    /// The link placeholders fall back to their localhost defaults, which is
    /// what a developer sees. These tests are about the applicant values, and
    /// an empty configuration keeps them from depending on a setting.
    /// </remarks>
    private static readonly IConfiguration NoOrigins =
        new ConfigurationBuilder().Build();

    /// <summary>An event with only the dates a test names.</summary>
    private static EventDetail Season(
        DateTimeOffset? startsAt = null,
        DateTimeOffset? registrationClosesAt = null) =>
        new(Guid.Empty, "mh2027", "MorganHacks 2027",
            StartsAt: startsAt,
            EndsAt: null,
            RegistrationOpensAt: null,
            RegistrationClosesAt: registrationClosesAt,
            DecisionsAnnouncedAt: null,
            Capacity: null,
            CreatedAt: default,
            CreatedBy: null);

    /// <summary>A form with just enough on it to render from.</summary>
    private static Form Paper(string kind = "application") =>
        new(Guid.Empty, Guid.Empty, "abc2def", "Apply to MorganHacks",
            Kind: kind,
            ClosesAt: null,
            RequiresSignIn: false,
            EligibleStatuses: []);

    /// <summary>One question on that form.</summary>
    private static FormField Question(string key, FieldType type = FieldType.ShortText) =>
        new() { Key = key, Type = type, Label = $"What is your {key}?" };

    /// <summary>A published version carrying those questions and nothing else.</summary>
    private static FormVersion Published(params FormField[] questions) =>
        new(Guid.Empty, Guid.Empty, 3, "published", questions, default, default);

    /// <summary>
    /// What a bound form contributes to the catalogue, and nobody's answers.
    /// </summary>
    /// <remarks>
    /// Through <see cref="MergeFields.QuestionsOn"/> rather than by building an
    /// <see cref="AnswerQuestion"/> list by hand, because which questions
    /// become names is the thing under test in half of these.
    /// </remarks>
    private static FormAnswers Asks(string kind, params FormField[] questions) =>
        FormAnswers.Asked(MergeFields.QuestionsOn(Paper(kind), Published(questions)));

    /// <summary>
    /// The same, plus what one person answered.
    /// </summary>
    /// <remarks>
    /// The answers are written as JSON text rather than as C# values, for the
    /// reason <c>AnswerSegmentTests</c> gives: the cases that matter are a
    /// number posted as <c>"4"</c> and a tick posted as <c>true</c>, and
    /// writing them as objects would let the serializer decide the thing under
    /// test.
    /// </remarks>
    private static FormAnswers Answered(
        string email, params (string Key, string Json)[] answers) =>
        Asks("survey", [.. answers.Select(answer => Question(answer.Key))]) with
        {
            Given = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(
                StringComparer.OrdinalIgnoreCase)
            {
                [email] = answers.ToDictionary(
                    answer => answer.Key,
                    answer => JsonDocument.Parse(answer.Json).RootElement.Clone(),
                    StringComparer.Ordinal),
            },
        };

    /// <summary>The address a member built here is addressed to.</summary>
    /// <remarks>
    /// Named rather than typed out twice, because an answer lookup is keyed on
    /// the recipient's address — so a test whose fixture and whose member
    /// disagreed about it would be asserting that nobody answered.
    /// </remarks>
    private const string Someone = "someone@example.invalid";

    private static SegmentMember Member(params (string Column, object? Value)[] answers) =>
        Member(Someone, answers);

    private static SegmentMember Member(
        string email, params (string Column, object? Value)[] answers) =>
        new(null,
            email,
            answers.ToDictionary(
                answer => answer.Column, answer => answer.Value, StringComparer.Ordinal));

    [Fact]
    public void Every_placeholder_is_offered_with_something_beside_it()
    {
        // A name with nothing beside it is a name somebody has to guess at,
        // and the description is what the editor's autocomplete shows.
        Assert.NotEmpty(MergeFields.All);
        Assert.All(MergeFields.All, field =>
            Assert.False(string.IsNullOrWhiteSpace(field.Description)));
    }

    [Fact]
    public void A_withheld_column_says_why()
    {
        // The sentence is the useful half. A catalogue that only recorded "no"
        // would leave the next person adding a column with nothing to reason
        // from, which is how a column ends up offered by accident.
        Assert.All(ApplicantColumns.Withheld.Values, reason =>
            Assert.False(string.IsNullOrWhiteSpace(reason)));
    }

    [Fact]
    public void A_column_name_becomes_a_placeholder_name_one_way()
    {
        Assert.Equal("firstName", MergeFields.NameFor("first_name"));
        Assert.Equal("levelOfStudy", MergeFields.NameFor("level_of_study"));
        Assert.Equal("email", MergeFields.NameFor("email"));
    }

    [Fact]
    public void A_boolean_reads_as_yes_or_no()
    {
        // Not "True", which is what .NET would hand back and what no email has
        // ever contained.
        Assert.Equal(
            "yes", MergeFields.Values(Member(("first_time_hacker", true)), NoOrigins)["firstTimeHacker"]);

        Assert.Equal(
            "no", MergeFields.Values(Member(("first_time_hacker", false)), NoOrigins)["firstTimeHacker"]);
    }

    [Fact]
    public void A_year_reads_as_digits_and_nothing_else()
    {
        // 2027, never 2,027. A separator here is a number that looks like a
        // price in the middle of a sentence about school.
        Assert.Equal(
            "2027", MergeFields.Values(Member(("graduation_year", 2027)), NoOrigins)["graduationYear"]);
    }

    [Fact]
    public void Text_arrives_as_it_was_typed()
    {
        Assert.Equal(
            "Morgan State University",
            MergeFields.Values(Member(("school", "Morgan State University")), NoOrigins)["school"]);
    }

    [Fact]
    public void Nothing_at_all_is_reported_as_a_gap_rather_than_filled_in_empty()
    {
        // The case the whole gap check is for, now across every type: a null
        // integer, a null boolean, text nobody typed into, and a column the
        // segment did not carry are the same thing to a reader, and all four
        // must leave the placeholder standing so the preview can count it.
        var member = Member(
            ("email", "someone@example.invalid"),
            ("first_name", null),
            ("school", "   "),
            ("graduation_year", null),
            ("first_time_hacker", null));

        var values = MergeFields.Values(member, NoOrigins);

        Assert.Equal("someone@example.invalid", values["email"]);
        Assert.False(values.ContainsKey("firstName"));
        Assert.False(values.ContainsKey("school"));
        Assert.False(values.ContainsKey("graduationYear"));
        Assert.False(values.ContainsKey("firstTimeHacker"));

        // Including shirtSize, which this member has no entry for at all.
        Assert.Equal(
            ["firstName", "graduationYear", "school", "shirtSize"],
            MergeFields.Unfilled(
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "email", "firstName", "school", "graduationYear", "shirtSize",
                },
                member,
                NoOrigins));
    }

    [Fact]
    public void A_placeholder_for_a_column_that_does_not_exist_is_not_offered_or_fillable()
    {
        // resume_key is a real column and deliberately withheld; nickname is
        // not a column at all. Neither is offered, and neither is fillable, so
        // a template using one is refused before the send.
        var applicants = new Segment.InStatus(Guid.NewGuid(), [ApplicationStatus.Accepted]);
        var fillable = MergeFields.Fillable(applicants);
        var offered = MergeFields.All.Select(field => field.Name).ToList();

        foreach (var name in new[] { "resumeKey", "responses", "phone", "status", "nickname" })
        {
            Assert.DoesNotContain(name, offered);
            Assert.DoesNotContain(name, fillable);
        }
    }

    [Fact]
    public void A_list_of_addresses_can_fill_nothing_it_was_not_given()
    {
        // The rule is not "only the address" — it is that a typed list carries
        // only what whoever built it supplied, so nothing read out of an
        // application can be filled. Our own links are the same sentence for
        // everybody in the send and are unaffected by who is receiving it, so
        // they stay offered.
        //
        // The names are the exception, and the reason the shape carries them:
        // an imported list is usually an export of people we mailed last year,
        // and it has their names in two columns beside the address. The rest
        // of the catalogue is still out, because a shirt size is on an
        // application and these recipients may not have one.
        var fillable = MergeFields.Fillable(
            new Segment.Addresses([new Segment.AddressRecipient(
                "someone@example.invalid", "Ada", "Lovelace")]));

        Assert.Contains("email", fillable);
        Assert.Contains("link.portal", fillable);
        Assert.Contains("firstName", fillable);
        Assert.Contains("lastName", fillable);

        foreach (var personal in new[] { "school", "graduationYear", "shirtSize" })
        {
            Assert.DoesNotContain(personal, fillable);
        }
    }

    /// <summary>
    /// Offering a name is about the shape, not about one list having them.
    /// </summary>
    /// <remarks>
    /// Fillable answers "can this kind of audience carry a name", because that
    /// is what decides whether the editor offers the placeholder at all.
    /// Whether this particular list has one for everybody is the coverage
    /// check's question, asked at send against the resolved members — which is
    /// what refuses the greeting template aimed at a list with a blank in it.
    /// </remarks>
    [Fact]
    public void A_list_with_no_names_still_offers_them_and_is_refused_later_instead()
    {
        var fillable = MergeFields.Fillable(
            new Segment.Addresses([new Segment.AddressRecipient(
                "sponsor@example.invalid", null, null)]));

        Assert.Contains("firstName", fillable);
    }

    [Fact]
    public void A_link_placeholder_is_filled_for_everybody_in_the_send()
    {
        // Not per-recipient, which is the point: it comes from configuration
        // rather than from the member, so a typed address list fills it too.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PublicBaseUrl"] = "https://portal.example.test/",
                ["FormsBaseUrl"] = "https://forms.example.test",
                ["ConsoleBaseUrl"] = "https://admin.example.test",
            })
            .Build();

        var values = MergeFields.Values(Member(("email", "a@example.invalid")), config);

        // The trailing slash is trimmed, because these are joined to paths.
        Assert.Equal("https://portal.example.test", values["link.portal"]);
        Assert.Equal("https://forms.example.test", values["link.forms"]);
        Assert.Equal("https://admin.example.test", values["link.console"]);
    }

    [Fact]
    public void An_event_date_reads_in_the_event_zone_with_the_zone_said_out_loud()
    {
        // The reason these exist now and did not before. Stored as an instant,
        // read as an evening in the event's city: 2027-01-16T04:59Z is the
        // fifteenth at 11:59 PM Eastern, and the fifteenth is the day the team
        // set and the day the flyer says.
        var season = Season(registrationClosesAt:
            new DateTimeOffset(2027, 1, 16, 4, 59, 0, TimeSpan.Zero));

        var values = MergeFields.Values(
            Member(("email", "a@example.invalid")), NoOrigins, season);

        Assert.Equal(
            "January 15, 2027 at 11:59 PM EST", values["event.registrationClosesAt"]);
    }

    [Fact]
    public void An_event_date_in_summer_says_EDT()
    {
        // The failure this class was written for: the 2026 deadline was
        // written up as EST in a month that was on EDT. July is daylight time,
        // so the abbreviation has to move with it rather than be assumed.
        var season = Season(startsAt:
            new DateTimeOffset(2027, 7, 10, 16, 0, 0, TimeSpan.Zero));

        var values = MergeFields.Values(
            Member(("email", "a@example.invalid")), NoOrigins, season);

        Assert.Equal("July 10, 2027 at 12:00 PM EDT", values["event.startsAt"]);
    }

    [Fact]
    public void A_date_nobody_has_set_leaves_the_placeholder_standing()
    {
        // An email promising a deadline the team has not agreed is worse than
        // one that cannot be sent, so an unset date stays out of the
        // dictionary and the coverage check refuses the send.
        var values = MergeFields.Values(
            Member(("email", "a@example.invalid")), NoOrigins, Season());

        Assert.False(values.ContainsKey("event.registrationClosesAt"));
        Assert.False(values.ContainsKey("event.startsAt"));
        Assert.Equal("MorganHacks 2027", values["event.name"]);
    }

    [Fact]
    public void With_no_event_at_all_every_event_placeholder_stands()
    {
        // What a fresh database has. The links still fill, because they do not
        // depend on a season existing.
        var values = MergeFields.Values(
            Member(("email", "a@example.invalid")), NoOrigins, season: null);

        Assert.DoesNotContain(values.Keys, key => key.StartsWith("event.", StringComparison.Ordinal));
        Assert.True(values.ContainsKey("link.portal"));
    }

    [Fact]
    public void A_saved_value_is_offered_under_its_prefix_and_fills_from_the_row()
    {
        var saved = new List<SavedValue>
        {
            new("discordInvite", "https://discord.gg/example", "The server everyone joins.", default),
        };

        var offered = MergeFields.Including(saved).Select(field => field.Name).ToList();
        Assert.Contains("saved.discordInvite", offered);

        var values = MergeFields.Values(
            Member(("email", "a@example.invalid")), NoOrigins, season: null, saved: saved);

        Assert.Equal("https://discord.gg/example", values["saved.discordInvite"]);
    }

    [Fact]
    public void A_saved_value_without_a_description_still_says_something()
    {
        // The editor lays a row out around a description, and a name with
        // nothing beside it is one somebody has to guess at. The row's own
        // sentence where there is one, a fallback where there is not.
        var saved = new List<SavedValue> { new("venue", "Room 214", null, default) };

        var field = Assert.Single(
            MergeFields.Including(saved), f => f.Name == "saved.venue");

        Assert.False(string.IsNullOrWhiteSpace(field.Description));
        Assert.Equal(MergeFields.Groups.Saved, field.Group);
    }

    [Fact]
    public void A_saved_value_cannot_stand_in_for_a_built_in_name()
    {
        // The collision story, which is structural rather than checked. The
        // prefix is reserved and 0045 forbids a dot in the name, so a row
        // called "portal" becomes {{saved.portal}} and can never be
        // {{link.portal}} however either set grows.
        var saved = new List<SavedValue> { new("portal", "https://evil.example", null, default) };

        var values = MergeFields.Values(
            Member(("email", "a@example.invalid")), NoOrigins, season: null, saved: saved);

        Assert.Equal("https://evil.example", values["saved.portal"]);
        Assert.Equal("http://localhost:3000", values["link.portal"]);
    }

    [Fact]
    public void A_form_placeholder_is_offered_only_to_a_template_that_names_one()
    {
        // The rule this whole file exists for, applied to a group rather than
        // a name: offering {{form.link}} to a template with no form bound
        // would be offering something the send refuses, discovered by whoever
        // approves it rather than whoever wrote it.
        Assert.DoesNotContain(
            MergeFields.Including([], aboutAForm: false),
            field => field.Group == MergeFields.Groups.Form);

        Assert.Contains(
            MergeFields.Including([], aboutAForm: true),
            field => field.Name == "form.link");
    }

    [Fact]
    public void A_form_link_is_built_from_the_forms_origin_and_the_code()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FormsBaseUrl"] = "https://forms.example.test/",
            })
            .Build();

        var values = MergeFields.Values(
            Member(("email", "a@example.invalid")), config,
            season: null, saved: null, paper: Paper());

        Assert.Equal("https://forms.example.test/abc2def", values["form.link"]);
        Assert.Equal("Apply to MorganHacks", values["form.name"]);
    }

    [Fact]
    public void A_template_whose_form_is_gone_leaves_every_form_placeholder_standing()
    {
        // Deleted, or removed by 0038. Both arrive here as null, and the
        // coverage check turns a standing placeholder into a refusal — which
        // beats mailing four hundred people a dead link.
        var values = MergeFields.Values(
            Member(("email", "a@example.invalid")), NoOrigins,
            season: null, saved: null, paper: null);

        Assert.DoesNotContain(values.Keys, k => k.StartsWith("form.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_link_placeholder_says_what_it_is()
    {
        // Same contract as every other placeholder: the editor shows this
        // beside the name, and a name with nothing beside it is one somebody
        // has to guess at.
        var links = MergeFields.All.Where(field => field.Group == MergeFields.Groups.Links);

        Assert.NotEmpty(links);
        Assert.All(links, field =>
        {
            Assert.StartsWith("link.", field.Name);
            Assert.False(string.IsNullOrWhiteSpace(field.Description));
            Assert.Null(field.Column);
        });
    }

    [Fact]
    public void A_form_placeholder_that_resolves_is_not_counted_as_a_gap()
    {
        // Unfilled forwarded everything to Values except the form, so every
        // {{form.*}} placeholder was unfilled for everybody — which meant a
        // template that named a form and used its link was a gap for the whole
        // segment and refused at send, with nothing on the screen to say why.
        // The coverage check and the render have to measure what the send will
        // actually write.
        var wanted = new HashSet<string>(StringComparer.Ordinal) { "form.link", "form.closesAt" };

        Assert.Equal(
            ["form.closesAt"],
            MergeFields.Unfilled(
                wanted, Member(("email", "a@example.invalid")), NoOrigins,
                season: null, saved: null, paper: Paper()));
    }

    // -------------------------------------------------- answers to the form ---

    [Fact]
    public void A_question_is_offered_only_to_a_template_that_names_the_form()
    {
        // The same narrowing the form group already had, extended rather than
        // worked around: the questions arrive with the form and disappear with
        // it, because a template that names no form has nobody's answers to
        // echo and offering the name would be offering what the send refuses.
        var asks = Asks("survey", Question("shirt_size"));

        Assert.DoesNotContain(
            MergeFields.Including([], aboutAForm: false, asks),
            field => field.Name == "form.answer.shirt_size");

        var offered = Assert.Single(
            MergeFields.Including([], aboutAForm: true, asks),
            field => field.Name == "form.answer.shirt_size");

        // Under its own heading, and described by the question's own wording —
        // which is the only thing that tells form.answer.q1 from
        // form.answer.q2 in a menu.
        Assert.Equal(MergeFields.Groups.Answers, offered.Group);
        Assert.Equal("What is your shirt_size?", offered.Description);
    }

    [Fact]
    public void An_answer_fills_in_for_the_person_who_gave_it()
    {
        // The feature in one assertion: "you told us your shirt size is
        // medium", with the answer coming off the row rather than out of a
        // sentence an organizer typed from memory.
        var values = MergeFields.Values(
            Member(("email", Someone)), NoOrigins,
            season: null, saved: null, paper: Paper("survey"),
            answers: Answered(Someone, ("shirt_size", "\"medium\"")));

        Assert.Equal("medium", values["form.answer.shirt_size"]);
    }

    [Fact]
    public void An_address_is_matched_whatever_case_it_is_stored_in()
    {
        // applications.email keeps what somebody typed and the dedupe index is
        // on lower(email), so the address a segment hands back and the one a
        // lookup keyed on are routinely the same address in different cases.
        var values = MergeFields.Values(
            Member("Someone@Example.INVALID", ("email", Someone)), NoOrigins,
            season: null, saved: null, paper: Paper("survey"),
            answers: Answered(Someone, ("shirt_size", "\"medium\"")));

        Assert.Equal("medium", values["form.answer.shirt_size"]);
    }

    [Fact]
    public void Somebody_who_never_answered_leaves_the_placeholder_standing()
    {
        // Not substituted empty, which is the whole rule here and the same one
        // a null applicant column gets. "You told us your shirt size is" and
        // then nothing is worse than a send that is refused, so the value
        // stays out of the dictionary and the coverage check counts it.
        var answers = Answered(Someone, ("shirt_size", "\"medium\""));

        var absent = Member("grace@example.invalid", ("email", "grace@example.invalid"));

        Assert.False(
            MergeFields.Values(absent, NoOrigins, null, null, Paper("survey"), answers)
                .ContainsKey("form.answer.shirt_size"));

        Assert.Equal(
            ["form.answer.shirt_size"],
            MergeFields.Unfilled(
                new HashSet<string>(StringComparer.Ordinal) { "email", "form.answer.shirt_size" },
                absent, NoOrigins, null, null, Paper("survey"), answers));
    }

    [Theory]
    // A choice, a short answer and a date are all strings in the jsonb.
    [InlineData("\"medium\"", "medium")]
    // A tick is "yes", never "True" — the same word a boolean column renders
    // as, because an organizer must not get two words for one thing.
    [InlineData("true", "yes")]
    [InlineData("false", "no")]
    // A number with no thousands separator, like graduationYear: 2027 and
    // never 2,027.
    [InlineData("2027", "2027")]
    // A number input posts a string, which is a number to a reader either way.
    [InlineData("\"21\"", "21")]
    // Stored as typed rather than parsed and reformatted, so a price keeps its
    // cents.
    [InlineData("3.50", "3.50")]
    // Trimmed: this is what a browser posted rather than what a normaliser
    // wrote, and a stray space is visible in the middle of a sentence.
    [InlineData("\"  medium  \"", "medium")]
    public void Each_shape_of_answer_reads_as_a_person_would_write_it(string json, string reads)
    {
        var values = MergeFields.Values(
            Member(("email", Someone)), NoOrigins,
            season: null, saved: null, paper: Paper("survey"),
            answers: Answered(Someone, ("answer", json)));

        Assert.Equal(reads, values["form.answer.answer"]);
    }

    [Theory]
    // One ticked box is one value, with nothing joining it to anything.
    [InlineData("""["hardware"]""", "hardware")]
    [InlineData("""["hardware","design"]""", "hardware and design")]
    [InlineData("""["hardware","design","games"]""", "hardware, design and games")]
    // Each element reads the same way a bare answer does, so a list of
    // numbers is not a special case.
    [InlineData("[1,2]", "1 and 2")]
    // A blank element is dropped rather than left as a hole between commas.
    [InlineData("""["hardware","   ","games"]""", "hardware and games")]
    public void A_multi_select_reads_as_a_sentence_rather_than_as_JSON(string json, string reads)
    {
        // ["hardware","design"] in the middle of an email is us showing the
        // reader our schema. A checkbox question is one answer with several
        // parts, and quoting it back inside a sentence is the point of the
        // feature.
        var values = MergeFields.Values(
            Member(("email", Someone)), NoOrigins,
            season: null, saved: null, paper: Paper("survey"),
            answers: Answered(Someone, ("track", json)));

        Assert.Equal(reads, values["form.answer.track"]);
    }

    [Theory]
    // Text nobody typed into, which is the same absence as no answer at all.
    [InlineData("\"   \"")]
    // Nothing ticked. SubmissionValidation reads an empty array as unanswered
    // and so does this, because an answer of none is not an answer.
    [InlineData("[]")]
    [InlineData("null")]
    // The one object-shaped answer is an upload, and what it holds is where
    // the file went. InTheAnswerSet keeps those out of the catalogue; this is
    // the line under it, because an upload id rendered into a body is
    // applications.resume_key leaving the schema by another door.
    [InlineData("""{"upload":"5d4b8f2a-0000-4000-8000-000000000001"}""")]
    public void An_answer_with_nothing_in_it_is_a_gap_rather_than_an_empty_value(string json)
    {
        var values = MergeFields.Values(
            Member(("email", Someone)), NoOrigins,
            season: null, saved: null, paper: Paper("survey"),
            answers: Answered(Someone, ("answer", json)));

        Assert.False(values.ContainsKey("form.answer.answer"));
    }

    [Fact]
    public void A_question_keyed_link_does_not_collide_with_the_form_link()
    {
        // Why the prefix has three segments. A form asking "where can we find
        // your work" keyed `link` is ordinary, and under {{form.<key>}} it
        // would take over the share URL — or be taken over by it, depending on
        // which loop ran last. Structural rather than checked: DraftKeys holds
        // a key to ^[a-z][a-z0-9_]{0,62}$, so a key can never hold a dot and
        // can never climb out of this namespace.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FormsBaseUrl"] = "https://forms.example.test",
            })
            .Build();

        var answers = Answered(
            Someone,
            ("link", "\"https://ada.example/portfolio\""),
            ("name", "\"Ada\""));

        var offered = MergeFields.Including([], aboutAForm: true, answers)
            .Select(field => field.Name)
            .ToList();

        Assert.Contains("form.link", offered);
        Assert.Contains("form.answer.link", offered);
        Assert.Contains("form.name", offered);
        Assert.Contains("form.answer.name", offered);

        var values = MergeFields.Values(
            Member(("email", Someone)), config,
            season: null, saved: null, paper: Paper("survey"), answers: answers);

        Assert.Equal("https://forms.example.test/abc2def", values["form.link"]);
        Assert.Equal("https://ada.example/portfolio", values["form.answer.link"]);
        Assert.Equal("Apply to MorganHacks", values["form.name"]);
        Assert.Equal("Ada", values["form.answer.name"]);
    }

    [Fact]
    public void An_answer_the_catalogue_never_offered_is_not_filled_in()
    {
        // The one list read twice, in the direction that is easy to get wrong.
        // A form's author can add a question at any moment, so a row can hold
        // an answer under a key no editor was ever shown — and filling it
        // would mean the send writing a name the catalogue does not know.
        var answers = Asks("survey", Question("shirt_size")) with
        {
            Given = new Dictionary<string, IReadOnlyDictionary<string, JsonElement>>(
                StringComparer.OrdinalIgnoreCase)
            {
                [Someone] = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["shirt_size"] = JsonSerializer.SerializeToElement("medium"),
                    ["added_later"] = JsonSerializer.SerializeToElement("whatever"),
                },
            },
        };

        var values = MergeFields.Values(
            Member(("email", Someone)), NoOrigins,
            season: null, saved: null, paper: Paper("survey"), answers: answers);

        Assert.Equal("medium", values["form.answer.shirt_size"]);
        Assert.False(values.ContainsKey("form.answer.added_later"));
    }

    [Fact]
    public void A_typed_list_of_addresses_is_offered_no_answers_at_all()
    {
        // Same rule as the applicant columns rather than a second one: these
        // recipients are sponsors and mentors this system has never heard of,
        // so there is no answer of theirs to echo. The form's own link and name
        // are still offered, because they do not depend on who is receiving it.
        var answers = Asks("survey", Question("shirt_size"));

        var fillable = MergeFields.Fillable(
            new Segment.Addresses([new Segment.AddressRecipient(
                "sponsor@example.invalid", null, null)]),
            saved: null, aboutAForm: true, answers: answers);

        Assert.DoesNotContain("form.answer.shirt_size", fillable);
        Assert.Contains("form.link", fillable);

        Assert.Contains(
            "form.answer.shirt_size",
            MergeFields.Fillable(
                new Segment.InStatus(Guid.NewGuid(), [ApplicationStatus.Accepted]),
                saved: null, aboutAForm: true, answers: answers));
    }

    [Fact]
    public void A_long_answer_is_offered_even_though_it_cannot_pick_an_audience()
    {
        // The one place the merge catalogue is deliberately wider than the
        // audience picker. A paragraph is excluded from segments because
        // matching part of one is a search that catches the people who said
        // the opposite — which is a judgement about audiences and says nothing
        // about whether an email may quote it back.
        Assert.Contains(
            MergeFields.Including([], aboutAForm: true, Asks("survey", Question("why", FieldType.Paragraph))),
            field => field.Name == "form.answer.why");
    }

    [Fact]
    public void An_upload_is_not_offered_because_there_is_no_answer_in_it()
    {
        // What is stored is where the file went, which is applications.
        // resume_key under another column — withheld from the mail by name,
        // because a storage key in an email is a way to read somebody's CV for
        // anybody it is forwarded to.
        Assert.DoesNotContain(
            MergeFields.Including([], aboutAForm: true, Asks("survey", Question("cv", FieldType.File))),
            field => field.Group == MergeFields.Groups.Answers);
    }

    [Fact]
    public void An_answer_kept_in_a_column_of_its_own_is_not_offered_on_an_application_form()
    {
        // The case that would otherwise be an offered name no send could fill.
        // An application form's promoted answer is written to
        // applications.school and not also to responses, so
        // {{form.answer.school}} would be blank for everybody — and {{school}}
        // already exists for it, two rows up the same menu. A survey keeps
        // every answer in form_submissions.answers whatever the question says
        // about a column, so there the same question is offered: both
        // directions together, because this is the surprising claim.
        var school = Question("school") with
        {
            Storage = AnswerStorage.Column,
            Column = "school",
        };

        Assert.DoesNotContain(
            MergeFields.Including([], aboutAForm: true, Asks("application", school)),
            field => field.Group == MergeFields.Groups.Answers);

        Assert.Contains(
            MergeFields.Including([], aboutAForm: true, Asks("survey", school)),
            field => field.Name == "form.answer.school");
    }

    [Fact]
    public void A_form_nobody_has_published_offers_its_link_and_no_questions()
    {
        // Two different things, and both are honest. A link and a name belong
        // to the form rather than to a version of it, so they resolve from the
        // moment it exists; a question nobody is being asked yet has no answer
        // anybody could have given.
        var offered = MergeFields.Including(
                [], aboutAForm: true, FormAnswers.Asked(MergeFields.QuestionsOn(Paper(), null)))
            .Select(field => field.Name)
            .ToList();

        Assert.Contains("form.link", offered);
        Assert.DoesNotContain(offered, name => name.StartsWith("form.answer.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_question_with_wording_too_long_for_a_menu_is_cut_rather_than_dropped()
    {
        // MLH's data-sharing agreement is sixty words and a form can ask it. A
        // description is one line beside a name, and the alternative to cutting
        // it is a menu row that is one paragraph tall.
        var asked = Assert.Single(
            MergeFields.Including(
                [],
                aboutAForm: true,
                Asks("survey", Question("agree") with { Label = new string('x', 500) })),
            field => field.Group == MergeFields.Groups.Answers);

        Assert.True(asked.Description.Length < 100);
        Assert.EndsWith("…", asked.Description);
    }
}
