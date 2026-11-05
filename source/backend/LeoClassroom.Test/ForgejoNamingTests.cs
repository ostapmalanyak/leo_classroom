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
}
