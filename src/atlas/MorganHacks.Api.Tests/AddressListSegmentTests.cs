using System.Text.Json;
using MorganHacks.Applications.Segments;

namespace MorganHacks.Api.Tests;

/// <summary>
/// Reading and writing a typed list of recipients.
/// </summary>
/// <remarks>
/// No database and no fixture: this is the parse of a document and the
/// document it writes back, which is exactly the part that has to keep working
/// for campaigns drafted before names existed.
/// </remarks>
public class AddressListSegmentTests
{
    private static Segment Parse(string json)
    {
        Assert.True(
            Segment.TryParse(JsonDocument.Parse(json).RootElement, out var segment, out var error),
            $"Expected this to parse, and it said: {error}");

        return segment!;
    }

    private static string Refuse(string json)
    {
        Assert.False(
            Segment.TryParse(JsonDocument.Parse(json).RootElement, out _, out var error),
            "Expected this to be refused.");

        Assert.False(string.IsNullOrWhiteSpace(error), "A refusal needs a sentence somebody can act on.");
        return error!;
    }

    [Fact]
    public void A_recipient_carries_the_names_it_was_given()
    {
        var segment = Assert.IsType<Segment.Addresses>(Parse("""
            {"type":"explicitList","recipients":[
              {"email":"ada@example.invalid","firstName":"Ada","lastName":"Lovelace"}]}
            """));

        var recipient = Assert.Single(segment.Recipients);
        Assert.Equal("ada@example.invalid", recipient.Email);
        Assert.Equal("Ada", recipient.FirstName);
        Assert.Equal("Lovelace", recipient.LastName);
    }

    /// <summary>
    /// The shape this wrote before names existed still reads.
    /// </summary>
    /// <remarks>
    /// Every campaign drafted until names were added has a plain array of
    /// strings sitting in notify.campaigns.segment, and the entire point of
    /// storing that document is that it is still readable a month later.
    /// Dropping this branch would not make those campaigns old, it would make
    /// them unreadable.
    /// </remarks>
    [Fact]
    public void A_list_stored_before_names_existed_still_reads()
    {
        var segment = Assert.IsType<Segment.Addresses>(
            Parse("""{"type":"explicitList","emails":["sponsor@example.invalid"]}"""));

        var recipient = Assert.Single(segment.Recipients);
        Assert.Equal("sponsor@example.invalid", recipient.Email);
        Assert.Null(recipient.FirstName);
        Assert.Null(recipient.LastName);
    }

    [Fact]
    public void A_bare_string_among_the_recipients_is_an_address_with_no_names()
    {
        var segment = Assert.IsType<Segment.Addresses>(Parse("""
            {"type":"explicitList","recipients":["sponsor@example.invalid"]}
            """));

        Assert.Null(Assert.Single(segment.Recipients).FirstName);
    }

    /// <summary>
    /// A blank name is absent, not empty.
    /// </summary>
    /// <remarks>
    /// The two mean the same thing to everything downstream, and one spelling
    /// of nothing is what keeps the coverage check's count honest: an empty
    /// string written into the merge fields would read as filled and go out as
    /// "Hi ,".
    /// </remarks>
    [Fact]
    public void A_blank_name_is_stored_as_absent()
    {
        var segment = Assert.IsType<Segment.Addresses>(Parse("""
            {"type":"explicitList","recipients":[
              {"email":"a@example.invalid","firstName":"   ","lastName":""}]}
            """));

        var recipient = Assert.Single(segment.Recipients);
        Assert.Null(recipient.FirstName);
        Assert.Null(recipient.LastName);
    }

    /// <summary>
    /// One entry per address, first spelling wins, names and all.
    /// </summary>
    /// <remarks>
    /// These come out of a spreadsheet, where the same person in two
    /// capitalisations is ordinary. A second row is a duplicate rather than a
    /// correction and nothing here could tell which was meant, so the first is
    /// kept whole rather than merged with the second.
    /// </remarks>
    [Fact]
    public void One_person_listed_twice_is_one_recipient_with_the_first_names()
    {
        var segment = Assert.IsType<Segment.Addresses>(Parse("""
            {"type":"explicitList","recipients":[
              {"email":"Ada@Example.invalid","firstName":"Ada"},
              {"email":"ada@example.invalid","firstName":"Augusta"}]}
            """));

        var recipient = Assert.Single(segment.Recipients);
        Assert.Equal("Ada@Example.invalid", recipient.Email);
        Assert.Equal("Ada", recipient.FirstName);
    }

    /// <summary>
    /// What is stored can be read back as what it was.
    /// </summary>
    /// <remarks>
    /// The property the whole stored-segment arrangement rests on: lark keeps
    /// the document and never looks inside it, so the only thing making it
    /// meaningful later is that this can parse what it wrote.
    /// </remarks>
    [Fact]
    public void What_is_written_is_what_comes_back()
    {
        var original = new Segment.Addresses([
            new Segment.AddressRecipient("ada@example.invalid", "Ada", "Lovelace"),
            new Segment.AddressRecipient("sponsor@example.invalid", null, null),
        ]);

        var round = Assert.IsType<Segment.Addresses>(Parse(original.ToJson()));

        Assert.Equal(original.Recipients, round.Recipients);
    }

    [Fact]
    public void A_name_long_enough_to_be_a_mis_mapped_column_is_refused_by_address()
    {
        var error = Refuse($$"""
            {"type":"explicitList","recipients":[
              {"email":"a@example.invalid","firstName":"{{new string('x', 101)}}"}]}
            """);

        // Named back, because with four hundred rows on screen the one that is
        // wrong is the only useful thing to say.
        Assert.Contains("a@example.invalid", error);
    }

    [Fact]
    public void A_blank_address_is_refused_rather_than_dropped()
    {
        Refuse("""{"type":"explicitList","recipients":[{"email":"  "}]}""");
        Refuse("""{"type":"explicitList","recipients":[]}""");
        Refuse("""{"type":"explicitList"}""");
    }

    [Fact]
    public void A_recipient_that_is_neither_a_string_nor_an_object_is_refused()
    {
        Refuse("""{"type":"explicitList","recipients":[42]}""");
    }

    [Fact]
    public void More_recipients_than_a_campaign_is_for_is_refused_before_the_rows_exist()
    {
        var many = string.Join(",", Enumerable
            .Range(0, Segment.MaxRecipients + 1)
            .Select(i => $"\"person{i}@example.invalid\""));

        var error = Refuse($$"""{"type":"explicitList","recipients":[{{many}}]}""");
        Assert.Contains("10,000", error);
    }
}
