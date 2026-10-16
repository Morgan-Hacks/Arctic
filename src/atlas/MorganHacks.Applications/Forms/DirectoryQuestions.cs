using System.Text.Json;
using MorganHacks.Directory.Contracts;
namespace MorganHacks.Applications.Forms;

public static class DirectoryQuestions
{
    public static IReadOnlyList<FormField> All { get; } = [
        new() { Key="directory_section", Type=FieldType.Section, Label="Your hacker profile", Help="Only shown to other RSVP'd hackers, and only if you opt in when you RSVP." },
        Choice("directory_skills","Skills",Catalogues.Skills,true),
        new() { Key="directory_contact_links",Type=FieldType.ContactLinks,Label="Contact links",Help="Add up to 3 links, one per kind." },
        Text("headline","Headline",160), Text("pronouns","Pronouns",30),
        Choice("directory_interests","Interests or tracks",Catalogues.Interests,true),
        Text("build_idea","What I want to build",280),Text("looking_for","What I'm looking for in teammates",200),
        Choice("directory_pace","Weekend pace",Catalogues.Paces,false),
    ];
    private static FormField Text(string key, string label, int max) => new() { Key = "directory_" + key, Type = FieldType.ShortText, Label = label, MaxLength = max };
    private static FormField Choice(string key, string label, string[] choices, bool multiple) => new() { Key = key, Label = label, Type = multiple ? FieldType.Checkboxes : FieldType.Select, Options = choices.Select(x => new FieldOption(x, x)).ToArray() };

    public static ProfileFields Read(IReadOnlyDictionary<string, JsonElement> answers)
    {
        string? Text(string key) => answers.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        string[] List(string key) => answers.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString()! : "").ToArray() : [];
        return new() { Headline = Text("directory_headline"), Pronouns = Text("directory_pronouns"), BuildIdea = Text("directory_build_idea"), LookingFor = Text("directory_looking_for"), Pace = Text("directory_pace"), Skills = List("directory_skills"), Interests = List("directory_interests"), ContactLinks = List("directory_contact_links").Select(s => { var parts = s.Split('|', 2); return new ContactLink(parts[0], parts.Length == 2 ? parts[1] : ""); }).ToArray() };
    }
    public static IReadOnlyList<FormProblem> Check(IReadOnlyList<FormField> fields, IReadOnlyDictionary<string, JsonElement> answers)
    {
        var contactProblems = fields.Where(f => f.Type == FieldType.ContactLinks && f.Key != "directory_contact_links")
            .SelectMany(f =>
            {
                if (!answers.TryGetValue(f.Key, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return Array.Empty<FormProblem>();
                if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) return new[] { new FormProblem("Use a list of contact links.", f.Key) };
                return ProfileValidation.Check(Read(new Dictionary<string, JsonElement> { { "directory_contact_links", value } })).Errors.Values.Select(e => new FormProblem(e[0], f.Key)).ToArray();
            });
        var selected = answers.Where(a => fields.Any(f => f.Key == a.Key)).ToDictionary(a => a.Key, a => a.Value);
        var problems = ProfileValidation.Check(Read(selected)).Errors.Select(e => new FormProblem(e.Value[0], "directory_" + (e.Key switch { "contactLinks" => "contact_links", "buildIdea" => "build_idea", "lookingFor" => "looking_for", _ => e.Key }))).ToList();
        foreach (var field in fields.Where(f => f.Key.StartsWith("directory_", StringComparison.Ordinal) && f.Type != FieldType.Section))
        {
            if (!selected.TryGetValue(field.Key, out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
            bool list = field.Key is "directory_skills" or "directory_interests" or "directory_contact_links";
            if (list ? v.ValueKind != JsonValueKind.Array || v.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String) : v.ValueKind != JsonValueKind.String)
                problems.Add(new("Use the expected answer format.", field.Key));
        }
        problems.AddRange(contactProblems);
        return problems;
    }
    public static object? Normalized(FormField field, JsonElement value)
    {
        if (field.Type == FieldType.ContactLinks)
        {
            var f = Read(new Dictionary<string, JsonElement> { { "directory_contact_links", value } });
            return ProfileValidation.Check(f).Value.ContactLinks.Select(l => l.Kind + "|" + l.Value).ToArray();
        }
        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value;
    }
}
