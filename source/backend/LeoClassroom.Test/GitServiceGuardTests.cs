using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

/// <summary>
///     The guards that run before anything is handed to the git binary
/// </summary>
/// <remarks>
///     These cases must be rejected without git ever being invoked: git's <c>ext::</c> transport executes an
///     arbitrary helper command, <c>file://</c> would read the server's own filesystem, and a revision starting
///     with a dash is read by git as an option.
/// </remarks>
public sealed class GitServiceGuardTests
{
    private static GitService Build() =>
        new(Options.Create(new ForgejoSettings
        {
            BaseUrl = "https://git.test",
            AdminToken = "token",
            WebhookSecret = "secret",
            WebhookTargetUrl = "https://api.test/hook"
        }), Substitute.For<ILogger<GitService>>());

    [Theory]
    [InlineData("ext::sh -c 'touch /tmp/pwned'")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ssh://git@host/repo.git")]
    [InlineData("git://host/repo.git")]
    [InlineData("/etc/passwd")]
    [InlineData("--upload-pack=touch /tmp/pwned")]
    [InlineData("")]
    public async Task ExportRepositoryAsync_UnsupportedCloneUrl_IsRejected(string cloneUrl)
    {
        OneOf<Success, GitError> result = await Build().ExportRepositoryAsync(
            cloneUrl, Path.Combine(Path.GetTempPath(), $"leo-guard-{Guid.NewGuid():N}"), checkoutSha: null,
            TestContext.Current.CancellationToken);

        result.ShouldBe<GitError>().Reason.Should().Contain("http(s)");
    }

    [Fact]
    public void InternalCloneUrl_UsesBackendForgejoAddressAndKeepsRepositoryPath()
    {
        GitService service = Build();

        service.InternalCloneUrl("http://localhost:3000/poseoo-1/student.git")
               .Should().Be("https://git.test/poseoo-1/student.git");
    }

    [Theory]
    [InlineData("--upload-pack=touch /tmp/pwned")]
    [InlineData("HEAD; rm -rf /")]
    [InlineData("not-a-sha")]
    [InlineData("abc")]
    public async Task ExportRepositoryAsync_CheckoutShaThatIsNotAnObjectName_IsRejected(string checkoutSha)
    {
        OneOf<Success, GitError> result = await Build().ExportRepositoryAsync(
            "https://git.test/org/repo.git", Path.Combine(Path.GetTempPath(), $"leo-guard-{Guid.NewGuid():N}"),
            checkoutSha, TestContext.Current.CancellationToken);

        result.ShouldBe<GitError>().Reason.Should().Contain("object name");
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    [InlineData(".git/config")]
    [InlineData("src/.GIT/hooks/pre-commit")]
    [InlineData("C:\\secrets")]
    [InlineData("\\secrets")]
    [InlineData(".")]
    [InlineData("")]
    public async Task OverwriteFileAsync_PathOutsideTheRepository_IsRejected(string relativePath)
    {
        OneOf<Success, GitError> result = await Build().OverwriteFileAsync(
            "https://git.test/org/repo.git", relativePath, "content", "message",
            TestContext.Current.CancellationToken);

        result.ShouldBe<GitError>().Reason.Should().Contain("inside the repository");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/instructions.md")]
    public void OrdinaryRepositoryFile_IsAccepted(string relativePath)
    {
        RepositoryFilePath.IsSafe(relativePath).Should().BeTrue();
    }

    [Theory]
    [InlineData("ext::sh -c 'touch /tmp/pwned'")]
    [InlineData("file:///srv/secrets")]
    public async Task SeedRepositoryFromDirectoryAsync_UnsupportedCloneUrl_IsRejected(string cloneUrl)
    {
        OneOf<Success, GitError> result = await Build().SeedRepositoryFromDirectoryAsync(
            cloneUrl, Path.GetTempPath(), TestContext.Current.CancellationToken);

        result.ShouldBe<GitError>().Reason.Should().Contain("http(s)");
    }
}
