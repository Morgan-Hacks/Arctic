using MorganHacks.Directory.Domain;
namespace MorganHacks.Api.Tests;

public class DirectoryPhaseTests
{
    [Fact] public void A_missing_opening_date_never_opens_the_directory() => Assert.Equal("notOpen", new DirectorySettings(null, null).Phase(DateTimeOffset.UtcNow));
    [Fact] public void Archive_overrides_opening_date() => Assert.Equal("archived", new DirectorySettings(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow).Phase(DateTimeOffset.UtcNow));
}
