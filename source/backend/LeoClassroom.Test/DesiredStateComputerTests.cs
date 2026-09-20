using LeoClassroom.Services.Forgejo;
using LeoClassroom.Shared;

namespace LeoClassroom.Test;

public sealed class DesiredStateComputerTests
{
    private static readonly Instant Deadline = Instant.FromUtc(2026, 3, 1, 12, 0, 0);
    private const string Org = "algo-5";

    private static ReconciliationInput Input(DeadlineKind kind, bool revokesRead) => new(
        Org,
        ["IF_T"],
        [new AcceptanceInput("IF_S", Org, "algo-a1-if_s")],
        kind == DeadlineKind.None ? null : Deadline,
        kind,
        revokesRead);

    [Fact]
    public void NoDeadline_GrantsWrite()
    {
        DesiredState state = DesiredStateComputer.Compute(Input(DeadlineKind.None, false), Deadline.Plus(Duration.FromDays(1)));

        state.Collaborators.Should().Contain(c => c.Username == "IF_S" && c.Permission == CollaboratorPermission.Write);
        state.Collaborators.Should().Contain(c => c.Username == "IF_T" && c.Permission == CollaboratorPermission.Write);
        state.Org.Should().Be(Org);
        state.TeamMembers.Should().ContainSingle().Which.Should().Be("IF_T");
        state.TeamName.Should().Be(ForgejoNaming.TeachersTeamName);
    }

    [Fact]
    public void HardDeadline_BeforeDeadline_GrantsWrite()
    {
        DesiredState state = DesiredStateComputer.Compute(Input(DeadlineKind.Hard, true), Deadline.Minus(Duration.FromMinutes(1)));

        state.Collaborators.Should().Contain(c => c.Username == "IF_S" && c.Permission == CollaboratorPermission.Write);
        state.Collaborators.Should().Contain(c => c.Username == "IF_T" && c.Permission == CollaboratorPermission.Write);
    }

    [Fact]
    public void HardDeadline_AtDeadline_RevokesWriteToNone_WhenRevokeReadSet()
    {
        DesiredState state = DesiredStateComputer.Compute(Input(DeadlineKind.Hard, true), Deadline);

        state.Collaborators.Should().Contain(c => c.Username == "IF_S" && c.Permission == CollaboratorPermission.None);
        state.Collaborators.Should().Contain(c => c.Username == "IF_T" && c.Permission == CollaboratorPermission.Write);
    }

    [Fact]
    public void HardDeadline_AfterDeadline_LeavesReadWhenRevokeReadNotSet()
    {
        DesiredState state = DesiredStateComputer.Compute(Input(DeadlineKind.Hard, false), Deadline.Plus(Duration.FromHours(1)));

        state.Collaborators.Should().Contain(c => c.Username == "IF_S" && c.Permission == CollaboratorPermission.Read);
        state.Collaborators.Should().Contain(c => c.Username == "IF_T" && c.Permission == CollaboratorPermission.Write);
    }

    [Fact]
    public void SoftDeadline_AfterDeadline_LeavesWriteUnchanged()
    {
        DesiredState state = DesiredStateComputer.Compute(Input(DeadlineKind.Soft, true), Deadline.Plus(Duration.FromHours(1)));

        state.Collaborators.Should().Contain(c => c.Username == "IF_S" && c.Permission == CollaboratorPermission.Write);
        state.Collaborators.Should().Contain(c => c.Username == "IF_T" && c.Permission == CollaboratorPermission.Write);
    }

    [Fact]
    public void Computation_IsDeterministic_ForFixedInputs()
    {
        ReconciliationInput input = Input(DeadlineKind.Hard, true);
        Instant now = Deadline.Plus(Duration.FromHours(2));

        DesiredStateComputer.Compute(input, now).Should().BeEquivalentTo(DesiredStateComputer.Compute(input, now));
    }
}
