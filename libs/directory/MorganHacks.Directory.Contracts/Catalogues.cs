namespace MorganHacks.Directory.Contracts;

public static class Catalogues
{
    public const int MaxTeamSize = 4;
    public static readonly string[] Classifications = ["Freshman", "Sophomore", "Junior", "Senior", "Undergraduate (year not specified)", "Graduate student", "Postdoctoral", "High school", "Bootcamp / vocational", "Not currently a student", "Other", "Prefer not to say"];

    // Application study level does not tell us an undergraduate's class year.
    public static string? ClassificationFromStudyLevel(string? level) => level switch
    {
        "undergraduate-2y" or "undergraduate-3y" => "Undergraduate (year not specified)",
        "graduate" => "Graduate student",
        "post-doctorate" => "Postdoctoral",
        "secondary" => "High school",
        "bootcamp" or "vocational" => "Bootcamp / vocational",
        "not-a-student" => "Not currently a student",
        "other" or "less-than-secondary" => "Other",
        "prefer-not-to-say" => "Prefer not to say",
        _ => null,
    };
    public static readonly string[] Skills = ["Frontend", "Backend", "Full-stack", "Mobile", "UI/UX design", "Product management", "Data science", "Machine learning / AI", "Hardware / IoT", "Game development", "Cybersecurity", "DevOps / Cloud", "Pitching / presenting", "Python", "JavaScript / TypeScript", "React", "Node.js", "Java", "C / C++", "C#", "Swift", "Kotlin", "Flutter", "SQL", "Figma"];
    public static readonly string[] Paces = ["All-nighter", "Steady with breaks", "Here to learn"];
    public static readonly string[] LinkKinds = ["discord", "linkedin", "github", "website"];
    public static readonly string[] TeamStatuses = ["looking", "needMembers", "full", "solo"];
    public static readonly string[] Interests = ["Accessibility", "Education", "Environment", "Community", "Creative tools"];
}

public sealed record ContactLink(string Kind, string Value);

public sealed record ProfileFields
{
    public string? DisplayName { get; init; }
    public string? School { get; init; }
    public string? YearLevel { get; init; }
    public bool? FirstHackathon { get; init; }
    public string? Pronouns { get; init; }
    public string? Headline { get; init; }
    public string? BuildIdea { get; init; }
    public string? LookingFor { get; init; }
    public string? Pace { get; init; }
    public string? TeamStatus { get; init; }
    public int? TeammatesNeeded { get; init; }
    public string[] Skills { get; init; } = [];
    public string[] Interests { get; init; } = [];
    public ContactLink[] ContactLinks { get; init; } = [];
    public Guid? PhotoId { get; init; }

    public string[] MissingRequiredFields() => new[] {
        string.IsNullOrWhiteSpace(DisplayName) ? "displayName" : null,
        string.IsNullOrWhiteSpace(School) ? "school" : null,
        Skills.Length == 0 ? "skills" : null,
        ContactLinks.Length == 0 ? "contactLinks" : null,
    }.OfType<string>().ToArray();
}

public interface IDirectoryApplicantSource
{
    Task<bool> IsRsvpd(Guid personId, Guid eventId, CancellationToken ct);
    Task<Guid[]> GetRsvpdPersonIds(Guid eventId, CancellationToken ct);
    Task<ProfileFields?> GetProfileAnswers(Guid personId, Guid eventId, CancellationToken ct);
}
