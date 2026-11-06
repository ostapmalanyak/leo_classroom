using LeoClassroom.Services.Forgejo;

namespace LeoClassroom.Test;

public sealed class ForgejoNamingTests
{
    [Fact]
    public void UserDisplayName_PutsFamilyNameBeforeGivenNameWithoutSpaces()
    {
        string displayName = ForgejoNaming.UserDisplayName("Oleksandr", "Mukhin");

        displayName.Should().Be("MukhinOleksandr");
    }

    [Fact]
    public void UserDisplayName_TrimsWhitespaceAroundNames()
    {
        string displayName = ForgejoNaming.UserDisplayName(" Oleksandr ", " Mukhin ");

        displayName.Should().Be("MukhinOleksandr");
    }

    [Fact]
    public void OrgName_TruncatesLongCourseTitlesAndKeepsCourseId()
    {
        string orgName = ForgejoNaming.OrgName(new string('a', 200), 123);

        orgName.Should().Be(new string('a', 36) + "-123");
        orgName.Should().HaveLength(40);
    }

    [Fact]
    public void OrgName_UsesFallbackForTitlesWithoutSlugCharacters()
    {
        string orgName = ForgejoNaming.OrgName("你好", 123);

        orgName.Should().Be("course-123");
    }
}
