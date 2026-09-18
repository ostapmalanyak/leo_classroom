using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Services.Auth;
using LeoClassroom.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace LeoClassroom.Test;

public sealed class UserProvisioningCacheTests
{
    private const string StudentId = "IF123456";
    private static readonly Instant Start = Instant.FromUtc(2026, 3, 1, 8, 0, 0);

    private readonly IClock _clock = Substitute.For<IClock>();

    public UserProvisioningCacheTests() => _clock.GetCurrentInstant().Returns(Start);

    private UserProvisioningCache Build() => new(_clock, Substitute.For<ILogger<UserProvisioningCache>>());

    private static ClaimUserData Claims(
        string firstName = "Sam", string? email = "sam@school.at", Role? role = Role.Student,
        string studentId = StudentId) =>
        new(studentId, firstName, "Student", email, role, "5AHIF");

    private static ProvisionedUser Stored(
        string firstName = "Sam", string? email = "sam@school.at", Role role = Role.Student,
        UserState state = UserState.Active, string studentId = StudentId) =>
        new(studentId, firstName, "Student", email, role, "5AHIF", state);

    private void Advance(Duration by) => _clock.GetCurrentInstant().Returns(Start + by);

    [Fact]
    public void UnknownCaller_HasToBeReconciled()
    {
        Build().Lookup(Claims()).Should().Be(ProvisioningState.Unknown);
    }

    [Theory]
    [InlineData(ProvisioningState.Active)]
    [InlineData(ProvisioningState.Inactive)]
    public void RememberedState_IsReturnedForever(ProvisioningState state)
    {
        var cache = Build();
        cache.Remember(Claims(), state);

        // no expiry: provisioning is a once-per-user event, so the answer stays good until something changes it
        for (int i = 0; i < 1_000; i++)
        {
            cache.Lookup(Claims()).Should().Be(state);
        }
    }

    [Fact]
    public void DisabledCaller_IsRefusedFromMemory()
    {
        var cache = Build();
        cache.Remember(Claims(), ProvisioningState.Inactive);

        // the point of caching the negative answer: repeated attempts by a disabled account are not database load
        cache.Lookup(Claims()).Should().Be(ProvisioningState.Inactive);
    }

    [Theory]
    [InlineData("Samuel", "sam@school.at", Role.Student)]
    [InlineData("Sam", "new@school.at", Role.Student)]
    [InlineData("Sam", null, Role.Student)]
    [InlineData("Sam", "sam@school.at", Role.Teacher)]
    public void ChangedClaims_ForceAReconciliation(string firstName, string? email, Role role)
    {
        var cache = Build();
        cache.Remember(Claims(), ProvisioningState.Active);

        // a renamed, re-mailed or re-roled caller has to reach the database, otherwise their row goes stale forever
        cache.Lookup(Claims(firstName, email, role)).Should().Be(ProvisioningState.Unknown);
    }

    [Fact]
    public void ReconciledCaller_IsFreeAgainAfterwards()
    {
        var cache = Build();
        cache.Remember(Claims(), ProvisioningState.Active);

        ClaimUserData renamed = Claims(firstName: "Samuel");
        cache.Lookup(renamed).Should().Be(ProvisioningState.Unknown);
        cache.Remember(renamed, ProvisioningState.Active);

        cache.Lookup(renamed).Should().Be(ProvisioningState.Active);
    }

    [Fact]
    public void Forget_SendsACallerBackToTheDatabase()
    {
        var cache = Build();
        cache.Remember(Claims(), ProvisioningState.Active);

        cache.Forget(StudentId);

        cache.Lookup(Claims()).Should().Be(ProvisioningState.Unknown);
    }

    [Fact]
    public void Refresh_RecordsActiveAndDisabledUsersAlike()
    {
        var cache = Build();
        cache.Refresh([Stored(), Stored(studentId: "IF999999", state: UserState.SoftDeleted)]);

        cache.Lookup(Claims()).Should().Be(ProvisioningState.Active);
        cache.Lookup(Claims(studentId: "IF999999")).Should().Be(ProvisioningState.Inactive);
    }

    [Fact]
    public void Refresh_TurnsAKnownActiveUserIntoADisabledOne()
    {
        var cache = Build();
        cache.Remember(Claims(), ProvisioningState.Active);

        cache.Refresh([Stored(state: UserState.SoftDeleted)]);

        cache.Lookup(Claims()).Should().Be(ProvisioningState.Inactive);
    }

    [Fact]
    public void Refresh_DoesNotAnswerForACallerWhoseClaimsDivergeFromTheStoredRow()
    {
        var cache = Build();
        cache.Refresh([Stored(role: Role.Student)]);

        // promoted in Keycloak since the row was written: must reconcile rather than serve the stale answer
        cache.Lookup(Claims(role: Role.Teacher)).Should().Be(ProvisioningState.Unknown);
    }

    [Fact]
    public void ShouldAuditRefusal_FirstAttempt_IsRecorded()
    {
        var cache = Build();
        cache.Remember(Claims(), ProvisioningState.Inactive);

        cache.ShouldAuditRefusal(StudentId).Should().BeTrue();
    }

    [Fact]
    public void ShouldAuditRefusal_RepeatedAttemptsWithinTheWindow_AreNotRecorded()
    {
        var cache = Build();
        cache.Remember(Claims(), ProvisioningState.Inactive);
        cache.ShouldAuditRefusal(StudentId).Should().BeTrue();

        // a disabled account must not be able to decide how many rows the backend writes
        for (int i = 0; i < 100; i++)
        {
            cache.ShouldAuditRefusal(StudentId).Should().BeFalse();
        }
    }

    [Fact]
    public void ShouldAuditRefusal_AfterTheWindow_IsRecordedAgain()
    {
        var cache = Build();
        cache.Remember(Claims(), ProvisioningState.Inactive);
        cache.ShouldAuditRefusal(StudentId).Should().BeTrue();

        Advance(UserProvisioningCache.AuditThrottle);

        cache.ShouldAuditRefusal(StudentId).Should().BeTrue();
    }

    [Fact]
    public void ShouldAuditRefusal_UnknownCaller_IsRecorded()
    {
        // nothing to throttle against, and the caller is reconciling against the database anyway
        Build().ShouldAuditRefusal("IF999999").Should().BeTrue();
    }
}
