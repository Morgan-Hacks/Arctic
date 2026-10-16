using System.Text.Json;
using MorganHacks.Applications.Forms;
using MorganHacks.Directory.Contracts;
namespace MorganHacks.Api.Tests;

public class DirectoryValidationTests
{
    [Fact]
    public void Directory_starting_questions_have_saveable_keys()
    {
        Assert.Empty(DraftKeys.Check(DirectoryQuestions.All));
    }

    [Fact]
    public void Directory_answers_use_stable_form_keys()
    {
        var answers = new Dictionary<string, JsonElement>
        {
            ["directory_contact_links"] = JsonSerializer.SerializeToElement(new[] { "github|https://github.com/ada" }),
            ["directory_build_idea"] = JsonSerializer.SerializeToElement("A project"),
            ["directory_looking_for"] = JsonSerializer.SerializeToElement("Teammates")
        };
        Assert.Empty(SubmissionValidation.Check(DirectoryQuestions.All, answers));
        var profile = DirectoryQuestions.Read(answers);
        Assert.Equal("A project", profile.BuildIdea);
        Assert.Equal("Teammates", profile.LookingFor);
        var field = Assert.Single(DirectoryQuestions.All, f => f.Key == "directory_contact_links");
        var normalized = DirectoryQuestions.Normalized(field, answers[field.Key]);
        Assert.Equal(new[] { "github|ada" }, Assert.IsType<string[]>(normalized));
        Assert.Equal("ada", DirectoryQuestions.Read(new Dictionary<string, JsonElement>
        {
            [field.Key] = JsonSerializer.SerializeToElement(normalized)
        }).ContactLinks[0].Value);
    }

    [Theory]
    [InlineData("directory_contact_links")]
    [InlineData("directory_build_idea")]
    [InlineData("directory_looking_for")]
    public void Directory_validation_errors_use_form_keys(string key)
    {
        var value = key == "directory_contact_links"
            ? JsonSerializer.SerializeToElement(new[] { "website|javascript:alert(1)" })
            : JsonSerializer.SerializeToElement(new string('x', 281));
        var problems = DirectoryQuestions.Check(DirectoryQuestions.All,
            new Dictionary<string, JsonElement> { [key] = value });
        Assert.NotEmpty(problems);
        Assert.All(problems, problem => Assert.Equal(key, problem.FieldKey));
    }

    [Theory]
    [InlineData("Freshman", true)]
    [InlineData("Graduate student", true)]
    [InlineData("2028", false)]
    [InlineData("anything I want", false)]
    public void Classification_must_be_a_catalogue_choice(string value, bool valid)
    {
        Assert.Equal(valid, ProfileValidation.Check(new() { YearLevel = value }).Valid);
    }
    [Fact] public void Optional_application_section_can_be_empty() => Assert.Empty(SubmissionValidation.Check(DirectoryQuestions.All, new Dictionary<string, JsonElement>()));
    [Fact]
    public void Shared_validator_normalizes_github_and_whitespace()
    {
        var result = ProfileValidation.Check(new() { DisplayName = " Ada ", ContactLinks = [new("github", " https://github.com/ada ")] });
        Assert.True(result.Valid); Assert.Equal("Ada", result.Value.DisplayName); Assert.Equal("ada", result.Value.ContactLinks[0].Value);
    }
    [Theory]
    [InlineData("linkedin", "https://www.linkedin.com/company/morgan")]
    [InlineData("linkedin", "https://www.linkedin.com.evil.example/in/ada")]
    [InlineData("website", "javascript:alert(1)")]
    [InlineData("website", "http://example.com")]
    [InlineData("discord", "UPPERCASE")]
    [InlineData("discord", "x")]
    [InlineData("github", "bad/user")]
    [InlineData("unknown", "hello")]
    public void Unsafe_or_invalid_contacts_are_field_errors(string kind, string value) => Assert.Contains("contactLinks", ProfileValidation.Check(new() { ContactLinks = [new(kind, value)] }).Errors.Keys);
    [Fact]
    public void Duplicate_links_and_too_many_skills_are_refused()
    {
        var result = ProfileValidation.Check(new() { Skills = Catalogues.Skills.Take(6).ToArray(), ContactLinks = [new("github", "ada"), new("github", "grace")] });
        Assert.Contains("skills", result.Errors.Keys); Assert.Contains("contactLinks", result.Errors.Keys);
    }
    [Fact]
    public void All_text_limits_are_enforced_after_trimming()
    {
        var result = ProfileValidation.Check(new() { DisplayName = new('x', 201), School = new('x', 201), YearLevel = new('x', 101), Headline = new('x', 161), Pronouns = new('x', 31), BuildIdea = new('x', 281), LookingFor = new('x', 201) });
        Assert.Equal(7, result.Errors.Count);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    public void Four_person_teams_reject_counts_outside_one_to_three(int number) => Assert.Contains("teammatesNeeded", ProfileValidation.Check(new() { TeamStatus = "needMembers", TeammatesNeeded = number }).Errors.Keys);
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Four_person_teams_allow_one_to_three_needed(int number) => Assert.True(ProfileValidation.Check(new() { TeamStatus = "needMembers", TeammatesNeeded = number }).Valid);
    [Fact] public void Other_team_states_clear_the_count() => Assert.Null(ProfileValidation.Check(new() { TeamStatus = "full", TeammatesNeeded = 2 }).Value.TeammatesNeeded);
    [Fact]
    public void Complete_means_name_school_skill_and_contact()
    {
        var fields = new ProfileFields(); Assert.Equal(4, fields.MissingRequiredFields().Length);
        Assert.Empty((fields with { DisplayName = "Ada", School = "Morgan", Skills = ["Python"], ContactLinks = [new("github", "ada")] }).MissingRequiredFields());
    }
    [Fact]
    public void Bad_application_shapes_are_not_silently_dropped()
    {
        var answers = new Dictionary<string, JsonElement> { { "directory_skills", JsonSerializer.SerializeToElement(new { bad = "value" }) } };
        Assert.Contains(SubmissionValidation.Check(DirectoryQuestions.All, answers), p => p.FieldKey == "directory_skills");
    }
}
