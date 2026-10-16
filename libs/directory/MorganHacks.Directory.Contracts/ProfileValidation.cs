using System.Text.RegularExpressions;

namespace MorganHacks.Directory.Contracts;

public sealed record ProfileValidation(ProfileFields Value, IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Valid => Errors.Count == 0;
    public static ProfileValidation Check(ProfileFields input)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string key, string message) => errors[key] = [message];
        string? Text(string key, string? value, int max)
        {
            value = value?.Trim();
            if (value?.Length > max) Error(key, $"Use at most {max} characters.");
            return string.IsNullOrEmpty(value) ? null : value;
        }
        string[] Choices(string key, string[]? values, string[] catalogue)
        {
            values = values?.Select(v => v?.Trim() ?? "").ToArray() ?? [];
            if (values.Length > 5) Error(key, "Choose at most 5.");
            if (values.Any(v => !catalogue.Contains(v)) || values.Distinct().Count() != values.Length)
                Error(key, "Choose distinct values from the list.");
            return values;
        }
        var links = new List<ContactLink>();
        foreach (var link in input.ContactLinks ?? [])
        {
            if (link is null) { Error("contactLinks", "Choose a kind and enter a value."); continue; }
            var kind = link.Kind?.Trim() ?? "";
            var value = link.Value?.Trim() ?? "";
            bool valid = false;
            switch (kind)
            {
                case "discord": valid = Regex.IsMatch(value, @"^[a-z0-9_.]{2,32}$"); break;
                case "github":
                    if (Uri.TryCreate(value.Contains("://") ? value : "https://" + value, UriKind.Absolute, out var gh)
                        && gh.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                        && gh.UserInfo == "" && gh.Query == "" && gh.Fragment == "") value = gh.AbsolutePath.Trim('/');
                    valid = Regex.IsMatch(value, @"^[a-zA-Z0-9-]{1,39}$"); break;
                case "linkedin":
                    valid = Uri.TryCreate(value, UriKind.Absolute, out var li) && li.Scheme == "https"
                        && li.Host.Equals("www.linkedin.com", StringComparison.OrdinalIgnoreCase)
                        && li.IsDefaultPort && li.UserInfo == "" && li.Query == "" && li.Fragment == ""
                        && Regex.IsMatch(li.AbsolutePath, @"^/in/[a-zA-Z0-9_-]+/?$"); break;
                case "website":
                    valid = value.Length <= 200 && Uri.TryCreate(value, UriKind.Absolute, out var web)
                        && web.Scheme == "https" && web.Host != "" && web.UserInfo == ""; break;
            }
            if (!valid) Error("contactLinks", "Use a valid Discord username, GitHub username, LinkedIn /in/ URL, or HTTPS website.");
            links.Add(new(kind, value));
        }
        if (links.Count > 3 || links.Select(l => l.Kind).Distinct().Count() != links.Count)
            Error("contactLinks", "Add at most 3 links, one per kind.");
        var classification = Text("yearLevel", input.YearLevel, 100);
        if (classification is not null && !Catalogues.Classifications.Contains(classification))
            Error("yearLevel", "Choose a class classification from the list.");
        var pace = Text("pace", input.Pace, 40);
        if (pace is not null && !Catalogues.Paces.Contains(pace)) Error("pace", "Choose a weekend pace from the list.");
        var status = Text("teamStatus", input.TeamStatus, 20);
        if (status is not null && !Catalogues.TeamStatuses.Contains(status)) Error("teamStatus", "Choose a team status from the list.");
        if (status == "needMembers" && (input.TeammatesNeeded is null || input.TeammatesNeeded < 1 || input.TeammatesNeeded >= Catalogues.MaxTeamSize))
            Error("teammatesNeeded", $"Choose 1 to {Catalogues.MaxTeamSize - 1} teammates.");
        return new(input with
        {
            DisplayName = Text("displayName", input.DisplayName, 200),
            School = Text("school", input.School, 200),
            YearLevel = classification,
            Pronouns = Text("pronouns", input.Pronouns, 30),
            Headline = Text("headline", input.Headline, 160),
            BuildIdea = Text("buildIdea", input.BuildIdea, 280),
            LookingFor = Text("lookingFor", input.LookingFor, 200),
            Pace = pace,
            TeamStatus = status,
            TeammatesNeeded = status == "needMembers" ? input.TeammatesNeeded : null,
            Skills = Choices("skills", input.Skills, Catalogues.Skills),
            Interests = Choices("interests", input.Interests, Catalogues.Interests),
            ContactLinks = links.ToArray(),
        }, errors);
    }
}
