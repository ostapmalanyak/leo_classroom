using LeoClassroom.Shared;

namespace LeoClassroom.Services.Forgejo;

public sealed record AcceptanceInput(string StudentStudentId, string RepoOwner, string RepoName);

public sealed record ReconciliationInput(
    string Org,
    IReadOnlyList<string> TeacherStudentIds,
    IReadOnlyList<AcceptanceInput> Acceptances,
    Instant? Deadline,
    DeadlineKind DeadlineKind,
    bool HardDeadlineRevokesRead);

public sealed record DesiredCollaborator(string RepoOwner, string RepoName, string Username,
                                         CollaboratorPermission Permission);

public sealed record DesiredState(
    string Org,
    string TeamName,
    IReadOnlyList<string> TeamMembers,
    IReadOnlyList<DesiredCollaborator> Collaborators);

public static class DesiredStateComputer
{
    public static DesiredState Compute(ReconciliationInput input, Instant now)
    {
        CollaboratorPermission studentLevel = ComputeStudentLevel(input, now);

        List<DesiredCollaborator> collaborators = [.. input.Acceptances.Select(a =>
            new DesiredCollaborator(a.RepoOwner, a.RepoName, a.StudentStudentId, studentLevel))];

        return new DesiredState(input.Org, ForgejoNaming.TeachersTeamName, input.TeacherStudentIds, collaborators);
    }

    private static CollaboratorPermission ComputeStudentLevel(ReconciliationInput input, Instant now)
    {
        if (input.DeadlineKind != DeadlineKind.Hard || input.Deadline is null || now < input.Deadline.Value)
        {
            return CollaboratorPermission.Write;
        }

        return input.HardDeadlineRevokesRead ? CollaboratorPermission.None : CollaboratorPermission.Read;
    }
}
