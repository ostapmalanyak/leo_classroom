using LeoClassroom.Services;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Security;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class GitCredentialServiceTests
{
    private const string StudentId = "IF123456";
    private static readonly Instant Now = Instant.FromUtc(2026, 3, 1, 8, 0, 0);

    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IForgejoClient _forgejo = Substitute.For<IForgejoClient>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    public GitCredentialServiceTests()
    {
        _uow.UserRepository.Returns(_users);
        _currentUser.StudentId.Returns(StudentId);
        _forgejo.EnsureUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<long>())
                .Returns(new ValueTask<OneOf<Success, ForgejoError>>(new Success()));
        _forgejo.SetUserPasswordAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, NotFound, ForgejoError>>(new Success()));
    }

    private GitCredentialService Build(long authSourceId = 0)
    {
        var clock = Substitute.For<IClock>();
        clock.GetCurrentInstant().Returns(Now);

        return new GitCredentialService(_uow, _currentUser, _forgejo,
                                        Options.Create(new ForgejoSettings
                                        {
                                            BaseUrl = "http://f", AdminToken = "x", WebhookSecret = "w",
                                            WebhookTargetUrl = "http://f/hook", AuthSourceId = authSourceId
                                        }),
                                        clock, Substitute.For<ILogger<GitCredentialService>>());
    }

    private User ArrangeUser(Instant? issuedAt = null)
    {
        var user = new User
        {
            Id = 1, StudentId = StudentId, FirstName = "Sam", LastName = "Student", Email = "sam@school.at",
            Role = Role.Student, State = UserState.Active, GitCredentialIssuedAt = issuedAt
        };
        _users.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(user));

        return user;
    }

    [Fact]
    public async Task GetStatusAsync_NeverIssued_ReportsNoTimestamp()
    {
        ArrangeUser();

        var result = await Build().GetStatusAsync();

        GitCredentialStatus status = result.ShouldBe<GitCredentialStatus>();
        status.Username.Should().Be(StudentId);
        status.IssuedAt.Should().BeNull();
        status.Managed.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_ExternalLoginSource_ReportsUnmanaged()
    {
        ArrangeUser();

        var result = await Build(authSourceId: 7).GetStatusAsync();

        result.ShouldBe<GitCredentialStatus>().Managed.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_UnknownUser_IsNotFound()
    {
        _users.GetTrackedByStudentIdAsync(StudentId).Returns(new ValueTask<User?>(default(User?)));

        (await Build().GetStatusAsync()).ShouldBe<NotFound>();
    }

    [Fact]
    public async Task ResetAsync_CreatesTheAccountThenSetsThePassword()
    {
        ArrangeUser();

        var result = await Build().ResetAsync();

        result.ShouldBe<IssuedGitCredential>();

        // nothing else creates the Forgejo account when the LDAP sync is not running
        Received.InOrder(() =>
        {
            _forgejo.EnsureUserAsync(StudentId, "sam@school.at", 0);
            _forgejo.SetUserPasswordAsync(StudentId, Arg.Any<string>());
        });
    }

    [Fact]
    public async Task ResetAsync_ReturnsThePasswordAndStampsOnlyTheTime()
    {
        User user = ArrangeUser();

        var result = await Build().ResetAsync();

        IssuedGitCredential issued = result.ShouldBe<IssuedGitCredential>();
        issued.Username.Should().Be(StudentId);
        issued.Password.Should().NotBeNullOrWhiteSpace();
        issued.IssuedAt.Should().Be(Now);

        // the password itself is never persisted - only the fact that one was issued
        user.GitCredentialIssuedAt.Should().Be(Now);
        await _uow.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task ResetAsync_IssuesADifferentPasswordEachTime()
    {
        ArrangeUser();
        var sut = Build();

        string first = (await sut.ResetAsync()).ShouldBe<IssuedGitCredential>().Password;
        string second = (await sut.ResetAsync()).ShouldBe<IssuedGitCredential>().Password;

        second.Should().NotBe(first);
    }

    [Fact]
    public async Task ResetAsync_ExternalLoginSource_IsRefused()
    {
        ArrangeUser();

        var result = await Build(authSourceId: 7).ResetAsync();

        // the password would never be consulted, so issuing one would only mislead the user
        result.ShouldBe<ForgejoError>();
        await _forgejo.DidNotReceive().SetUserPasswordAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ResetAsync_ForgejoRefusesThePassword_DoesNotStampTheUser()
    {
        User user = ArrangeUser();
        _forgejo.SetUserPasswordAsync(Arg.Any<string>(), Arg.Any<string>())
                .Returns(new ValueTask<OneOf<Success, NotFound, ForgejoError>>(new ForgejoError(500, "nope")));

        var result = await Build().ResetAsync();

        result.ShouldBe<ForgejoError>();
        user.GitCredentialIssuedAt.Should().BeNull();
        await _uow.DidNotReceive().SaveChangesAsync();
    }
}

public sealed class GitCredentialGeneratorTests
{
    [Fact]
    public void Generate_ProducesAllCharacterClasses()
    {
        // Forgejo can be configured to demand them, and a rejected password would surface as a 500
        for (int i = 0; i < 200; i++)
        {
            string password = GitCredentialGenerator.Generate();

            password.Should().HaveLength(20);
            password.Should().MatchRegex("[a-z]").And.MatchRegex("[A-Z]").And.MatchRegex("[0-9]");
            password.Any(c => "!@#%^&*-_=+".Contains(c)).Should().BeTrue();
        }
    }

    [Fact]
    public void Generate_AvoidsCharactersThatAreMisreadWhenTypedByHand()
    {
        for (int i = 0; i < 200; i++)
        {
            GitCredentialGenerator.Generate().Should().NotContainAny("0", "O", "1", "l", "I");
        }
    }

    [Fact]
    public void Generate_IsNotPredictable()
    {
        HashSet<string> generated = [.. Enumerable.Range(0, 500).Select(_ => GitCredentialGenerator.Generate())];

        generated.Should().HaveCount(500);
    }
}
