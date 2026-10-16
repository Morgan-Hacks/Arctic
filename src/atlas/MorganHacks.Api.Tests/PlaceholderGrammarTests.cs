using System.Text.RegularExpressions;
using MorganHacks.Lark.Data.Domain;

namespace MorganHacks.Api.Tests;

/// <summary>
/// The renderer's idea of a placeholder against the editor's.
/// </summary>
/// <remarks>
/// These two have to agree and there is no compiler that can make them. The
/// renderer is a C# regex in <c>EmailTemplate.cs</c>; the editor is three
/// JavaScript ones in <c>portaladmin</c>. They disagreed for real, and the
/// shape of the failure is the reason this file exists rather than a comment:
/// the editor accepted a dot and the renderer did not, so <c>{{event.name}}</c>
/// was a placeholder to the menu, the "Unknown" panel and the author — and not
/// one to <see cref="TemplateRenderer.PlaceholdersIn"/>. Being invisible there
/// made it invisible to the campaign's unfillable and coverage checks too, so
/// the only path it could take was all the way out to everybody, as literal
/// braces, with every check reporting green.
/// <para>
/// The direction matters and is worth stating. The renderer must be **at least
/// as permissive** as the editor. Stricter, and anything the editor offers but
/// the renderer cannot see slips out unchecked, which is the bug above.
/// Looser is harmless: a name the editor would never produce is caught,
/// reported unknown, and refused.
/// </para>
/// <para>
/// Reading the TypeScript rather than restating it is deliberate, and it is the
/// same trade <see cref="MergeFieldSchemaTests"/> makes against
/// <c>information_schema</c>: a copy of the rule in the test is a third thing
/// to keep in step, and a test that only agrees with itself proves nothing.
/// </para>
/// </remarks>
public class PlaceholderGrammarTests
{
    /// <summary>
    /// Every editor-side file that decides what a placeholder name may contain,
    /// and the pattern in it that says so.
    /// </summary>
    /// <remarks>
    /// Three of them, because the editor does three different jobs with the
    /// same rule: recognising a name already typed, deciding whether what is
    /// being typed is still a name, and offering completions for it. All three
    /// are listed so that changing one and not the others fails here.
    /// </remarks>
    /// <remarks>
    /// Anchored on just enough of each line to find the right character class
    /// and no more. Matching the whole pattern would mean a second copy of it
    /// here, escaped twice over, that breaks on a reformat and teaches people
    /// the test is noise.
    /// </remarks>
    private static readonly (string Path, string Find)[] EditorRules =
    [
        // placeholdersIn: what the "Unknown" panel under the editor reads.
        ("src/portaladmin/components/templates/types.ts", @"\\\{\\\{\\s\*\(\[([^\]]+)\]\+\)"),

        // NAME: whether what has been typed so far is still a placeholder name.
        ("src/portaladmin/components/templates/placeholders.ts", @"NAME = /\^\[([^\]]+)\]"),

        // The CodeMirror completion source's trigger.
        ("src/portaladmin/components/templates/code-editor.tsx", @"matchBefore\(/\\\{\\\{\[([^\]]+)\]"),
    ];

    /// <summary>
    /// The characters the renderer will accept inside a pair of braces.
    /// </summary>
    /// <remarks>
    /// Read back out of the compiled regex rather than typed again here, so
    /// this cannot pass against a pattern the renderer no longer uses.
    /// </remarks>
    private static string RendererCharacterClass()
    {
        var pattern = RendererPattern();
        var match = Regex.Match(pattern, @"\[([^\]]+)\]");

        Assert.True(
            match.Success,
            $"The renderer's pattern has no character class in it: {pattern}. "
            + "If its shape changed, this test has to change with it.");

        return match.Groups[1].Value;
    }

    private static string RendererPattern()
    {
        // The pattern is a private [GeneratedRegex] property, so it is reached
        // through the regex it generated rather than through the attribute.
        var placeholder = typeof(TemplateRenderer).GetProperty(
            "Placeholder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(placeholder);
        var regex = Assert.IsAssignableFrom<Regex>(placeholder!.GetValue(null));
        return regex.ToString();
    }

    /// <summary>
    /// Walks up to the directory holding both <c>src</c> and <c>libs</c>.
    /// </summary>
    /// <remarks>
    /// The test runs from somewhere under <c>bin</c>, and how deep that is
    /// changes with the target framework and the configuration. Looking for a
    /// pair of directories that only the repository root has is steadier than
    /// counting how many times to go up.
    /// </remarks>
    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (System.IO.Directory.Exists(Path.Combine(directory.FullName, "src"))
                && System.IO.Directory.Exists(Path.Combine(directory.FullName, "libs")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"No directory above {AppContext.BaseDirectory} holds both src and libs, "
            + "so the editor's source cannot be read to compare against.");
    }

    [Fact]
    public void The_editor_and_the_renderer_allow_the_same_characters_in_a_name()
    {
        var root = RepositoryRoot();
        var renderer = RendererCharacterClass();

        foreach (var (path, find) in EditorRules)
        {
            var file = Path.Combine(root.FullName, path);

            Assert.True(
                File.Exists(file),
                $"{path} is gone. It held one of the editor's three placeholder "
                + "patterns; if it moved, update EditorRules so this keeps checking.");

            var match = Regex.Match(File.ReadAllText(file), find);

            Assert.True(
                match.Success,
                $"The placeholder pattern in {path} is no longer in the shape this "
                + "test looks for. That is not necessarily wrong, but it has to be "
                + "read again by hand and the search here updated — the risk this "
                + "test exists for is the two sides drifting unnoticed.");

            Assert.Equal(renderer, match.Groups[1].Value);
        }
    }

    [Fact]
    public void A_namespaced_name_is_a_placeholder()
    {
        // The bug itself, stated as a behaviour. Before the dot was allowed
        // this found nothing at all, and finding nothing is what let a dotted
        // name past every check a campaign makes.
        var template = Template(
            subject: "{{event.name}} closes soon",
            html: "<p>Apply: {{form.link}}</p>",
            text: "Apply: {{form.link}}");

        var found = TemplateRenderer.PlaceholdersIn(template);

        Assert.Contains("event.name", found);
        Assert.Contains("form.link", found);
    }

    [Fact]
    public void A_namespaced_name_is_filled_like_any_other()
    {
        var rendered = TemplateRenderer.Render(
            Template(
                subject: "{{event.name}}",
                html: "<p>{{form.link}}</p>",
                text: "{{form.link}}"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["event.name"] = "MorganHacks 2027",
                ["form.link"] = "https://forms.morganhacks.test/apply",
            });

        Assert.Equal("MorganHacks 2027", rendered.Subject);
        Assert.Contains("https://forms.morganhacks.test/apply", rendered.BodyText);
    }

    [Fact]
    public void An_unfillable_namespaced_name_is_left_standing()
    {
        // Same contract as every other placeholder: a value that is not there
        // reads as an obvious mistake rather than a sentence with a hole in
        // it. The campaign checks are what turn this into a refusal; the
        // renderer's job is only to leave the evidence.
        var rendered = TemplateRenderer.Render(
            Template(subject: "{{event.name}}", html: "<p>x</p>", text: "x"),
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Equal("{{event.name}}", rendered.Subject);
    }

    [Fact]
    public void A_dotted_value_is_escaped_in_the_html_like_any_other()
    {
        // Namespacing changes the name, not what a value is allowed to do on
        // arrival. An email client is a browser.
        var rendered = TemplateRenderer.Render(
            Template(subject: "s", html: "<p>{{saved.note}}</p>", text: "{{saved.note}}"),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["saved.note"] = "<script>alert(1)</script>",
            });

        Assert.DoesNotContain("<script>", rendered.BodyHtml);
        Assert.Contains("&lt;script&gt;", rendered.BodyHtml);
    }

    [Fact]
    public void A_namespaced_placeholder_survives_as_an_href()
    {
        // The sanitiser allows a scheme-less href, and the comment explaining
        // why names {{link}} — which reads as though that one name is special.
        // It is not: the rule is about the absence of a scheme, and a dot is
        // not a scheme. Worth pinning, because a template whose call to action
        // is {{form.link}} is the main thing this whole namespace is for, and
        // a sanitiser that quietly dropped the href would take the link out of
        // the email while leaving the words that promised it.
        var html = EmailHtml.Sanitize("""<a href="{{form.link}}">Apply now</a>""");

        Assert.Contains("{{form.link}}", html);
        Assert.Contains("Apply now", html);
    }

    private static EmailTemplate Template(string subject, string html, string text) =>
        new(
            Id: Guid.Empty,
            Key: "test",
            Kind: "broadcast",
            Subject: subject,
            BodyHtml: html,
            BodyText: text,
            FromLocal: "hello",
            FromDomain: "morganhacks.test",
            ReplyTo: null);
}
