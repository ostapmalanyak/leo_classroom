using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

/// <summary>
///     What deleting one user would destroy
/// </summary>
/// <param name="OwnAcceptances">Submissions made by this user</param>
/// <param name="CollateralAcceptances">
///     Submissions made by <em>other</em> students that would go with the courses and assignments this user owns.
///     This is the number that decides whether a deletion is acceptable, so it is reported separately.
/// </param>
/// <param name="AffectedStudents">How many other students lose at least one submission</param>
/// <param name="Repositories">Forgejo repositories that would be deleted</param>
/// <param name="DeletableFrom">
///     The date this user becomes eligible for the retention purge, or null when they are not soft-deleted and so
///     never become a candidate for it
/// </param>
public sealed record UserDeletionImpact(
    long UserId,
    string StudentId,
    string FirstName,
    string LastName,
    Role Role,
    UserState State,
    Instant? LdapLastSeen,
    int OwnedRosters,
    int OwnedCourses,
    int OwnedAssignments,
    int OwnAcceptances,
    int CollateralAcceptances,
    int AffectedStudents,
    int Repositories,
    LocalDate? DeletableFrom);

/// <param name="Failed">Users whose deletion threw; they are left untouched and the purge continues</param>
/// <param name="Cutoff">The newest last-seen date that was eligible for this run</param>
public sealed record PurgeOutcome(
    int Purged, int Failed, LocalDate Cutoff, IReadOnlyCollection<UserDeletionImpact> Deleted);

public interface IUserDeletionService
{
    /// <summary>
    ///     Reports what deleting this user would destroy, without changing anything
    /// </summary>
    public ValueTask<OneOf<UserDeletionImpact, NotFound>> PreviewAsync(long userId);

    /// <summary>
    ///     Permanently deletes this user and everything that cascades from them
    /// </summary>
    public ValueTask<OneOf<Success<UserDeletionImpact>, NotFound>> DeleteAsync(long userId);

    /// <summary>
    ///     Reports which users are past their retention, and what deleting each of them would destroy
    /// </summary>
    /// <remarks>
    ///     The retention window is not a parameter: it is <see cref="RetentionPolicy" />, so a preview and the
    ///     purge that follows it can never disagree about who is eligible.
    /// </remarks>
    public ValueTask<IReadOnlyCollection<UserDeletionImpact>> PreviewExpiredAsync();

    /// <summary>
    ///     Permanently deletes every user <see cref="PreviewExpiredAsync" /> reports
    /// </summary>
    public ValueTask<PurgeOutcome> PurgeExpiredAsync();
}

/// <summary>
///     Permanent deletion of a user and the full cascade behind them
/// </summary>
/// <remarks>
///     <para>
///         This is a cascading delete by design: a user takes their own submissions with them, and also the
///         rosters, courses and assignments they own - including every other student's submissions inside those.
///         <see cref="PreviewAsync" /> exists so that an administrator sees that blast radius before committing to
///         it, and <c>CollateralAcceptances</c> is the number to look at.
///     </para>
///     <para>
///         The audit trail survives: <c>AuditEvent</c> records the actor by IF number as text rather than by
///         foreign key, so the history of a deleted user's actions remains readable.
///     </para>
/// </remarks>
internal sealed class UserDeletionService(
    IUnitOfWork uow,
    IDeletionCascade deletion,
    IForgejoClient forgejo,
    IUserProvisioningCache provisioningCache,
    IAuditLog audit,
    IClock clock,
    IOptions<ForgejoSettings> forgejoSettings,
    ILogger<UserDeletionService> logger) : IUserDeletionService
{
    public async ValueTask<OneOf<UserDeletionImpact, NotFound>> PreviewAsync(long userId)
    {
        User? user = await uow.UserRepository.GetByIdAsync(userId);
        if (user is null)
        {
            return new NotFound();
        }

        return (await BuildPlanAsync(user)).Impact;
    }

    public async ValueTask<OneOf<Success<UserDeletionImpact>, NotFound>> DeleteAsync(long userId)
    {
        User? user = await uow.UserRepository.GetByIdAsync(userId);
        if (user is null)
        {
            return new NotFound();
        }

        DeletionPlan plan = await BuildPlanAsync(user);
        await ExecuteAsync(plan);

        return new Success<UserDeletionImpact>(plan.Impact);
    }

    public async ValueTask<IReadOnlyCollection<UserDeletionImpact>> PreviewExpiredAsync()
    {
        List<UserDeletionImpact> impacts = [];
        foreach (User candidate in await FindCandidatesAsync())
        {
            impacts.Add((await BuildPlanAsync(candidate)).Impact);
        }

        return impacts.AsReadOnly();
    }

    public async ValueTask<PurgeOutcome> PurgeExpiredAsync()
    {
        IReadOnlyCollection<User> candidates = await FindCandidatesAsync();

        List<UserDeletionImpact> deleted = [];
        int failed = 0;

        // deliberately one user at a time rather than one transaction over the batch: a single failure - a Forgejo
        // call, a constraint nobody anticipated - must not undo the users already purged, and the run reports what
        // it managed. This mirrors AssignmentAutoDeleteService.
        foreach (User candidate in candidates)
        {
            try
            {
                DeletionPlan plan = await BuildPlanAsync(candidate);
                await ExecuteAsync(plan);
                deleted.Add(plan.Impact);
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex, "Purging user {StudentId} failed; it was left in place", candidate.StudentId);
            }
        }

        LocalDate cutoff = RetentionPolicy.CutoffFor(clock.GetCurrentInstant()).ToZonedDateTime().Date;
        await audit.RecordAsync(AuditAction.RetentionPurge, "User", null, new Dictionary<string, string>
        {
            ["cutoff"] = cutoff.ToString(),
            ["purged"] = deleted.Count.ToString(),
            ["failed"] = failed.ToString()
        });

        return new PurgeOutcome(deleted.Count, failed, cutoff, deleted.AsReadOnly());
    }

    private async ValueTask<IReadOnlyCollection<User>> FindCandidatesAsync() =>
        await uow.UserRepository.GetPurgeCandidatesAsync(RetentionPolicy.CutoffFor(clock.GetCurrentInstant()));

    /// <summary>
    ///     Walks the cascade closure of a user without changing anything
    /// </summary>
    /// <remarks>
    ///     The order matters, because a roster cannot be deleted while a course still points at it: rosters pull in
    ///     the courses that use them, courses pull in their assignments, and assignments pull in every acceptance
    ///     on them.
    /// </remarks>
    private async ValueTask<DeletionPlan> BuildPlanAsync(User user)
    {
        IReadOnlyCollection<long> rosterIds = await uow.RosterRepository.GetIdsOwnedByAsync(user.Id);

        HashSet<long> courseIds = [.. await uow.CourseRepository.GetIdsOwnedByAsync(user.Id)];
        courseIds.UnionWith(await uow.CourseRepository.GetIdsUsingRostersAsync(rosterIds));

        HashSet<long> assignmentIds = [.. await uow.AssignmentRepository.GetIdsOwnedByAsync(user.Id)];
        assignmentIds.UnionWith(await uow.AssignmentRepository.GetIdsByCoursesAsync([.. courseIds]));

        IReadOnlyCollection<AcceptanceRepoRef> acceptances =
            await uow.AcceptanceRepository.GetForDeletionAsync(user.Id, [.. assignmentIds]);

        List<AcceptanceRepoRef> own = [.. acceptances.Where(a => a.StudentId == user.Id)];
        List<AcceptanceRepoRef> collateral = [.. acceptances.Where(a => a.StudentId != user.Id)];

        // the assignments this user owns that are not already inside a course that is going away
        HashSet<long> ownedAssignmentIds = [.. await uow.AssignmentRepository.GetIdsOwnedByAsync(user.Id)];
        ownedAssignmentIds.ExceptWith(await uow.AssignmentRepository.GetIdsByCoursesAsync([.. courseIds]));

        var impact = new UserDeletionImpact(
            user.Id, user.StudentId, user.FirstName, user.LastName, user.Role, user.State, user.LdapLastSeen,
            OwnedRosters: rosterIds.Count,
            OwnedCourses: courseIds.Count,
            OwnedAssignments: assignmentIds.Count,
            OwnAcceptances: own.Count,
            CollateralAcceptances: collateral.Count,
            AffectedStudents: collateral.Select(a => a.StudentId).Distinct().Count(),
            Repositories: acceptances.Count,
            DeletableFrom: user.State == UserState.SoftDeleted && user.LdapLastSeen is { } lastSeen
                ? RetentionPolicy.DeletableFrom(lastSeen)
                : null);

        return new DeletionPlan(user, [.. rosterIds], [.. courseIds], [.. ownedAssignmentIds], own, impact);
    }

    private async ValueTask ExecuteAsync(DeletionPlan plan)
    {
        logger.LogWarning(
            "Permanently deleting user {StudentId}: {Rosters} rosters, {Courses} courses, {Assignments} assignments, "
            + "{OwnAcceptances} own and {Collateral} other submissions across {Students} other students",
            plan.User.StudentId, plan.Impact.OwnedRosters, plan.Impact.OwnedCourses, plan.Impact.OwnedAssignments,
            plan.Impact.OwnAcceptances, plan.Impact.CollateralAcceptances, plan.Impact.AffectedStudents);

        // leaves upward, so that nothing loses the row that names its Forgejo counterpart before that counterpart
        // is gone: this user's own submissions, then the assignments and courses they own with everything inside
        // them, and only then the rosters those courses pointed at
        CascadeResult cascade = await deletion.DeleteAcceptancesAsync(plan.OwnAcceptances);
        cascade = cascade.Plus(await deletion.DeleteAssignmentsAsync(plan.OwnedAssignmentIds));
        cascade = cascade.Plus(await deletion.DeleteCoursesAsync(plan.CourseIds));

        await uow.RosterRepository.DeleteByIdsAsync(plan.RosterIds);

        // rows that exist only because this user does, deleted here rather than left to the database's cascade so
        // that the whole removal is visible in one place
        await uow.NotificationRepository.DeleteByRecipientAsync(plan.User.Id);
        await uow.NotificationPreferenceRepository.DeleteByUserAsync(plan.User.Id);

        await RemoveFromSurvivingTeacherTeamsAsync(plan.User);
        await DeactivateForgejoAccountAsync(plan.User.StudentId);

        // the remaining references to this user are pure link rows in the many-to-many tables, which mirror
        // nothing outside the database; those the database's cascade may take
        await uow.UserRepository.DeleteByIdAsync(plan.User.Id);

        // the cache answers for known users; a deleted one must not keep sailing through it
        provisioningCache.Forget(plan.User.StudentId);

        Dictionary<string, string> metadata = new(cascade.ToMetadata())
        {
            ["studentId"] = plan.User.StudentId,
            ["ownedRosters"] = plan.Impact.OwnedRosters.ToString(),
            ["ownAcceptances"] = plan.Impact.OwnAcceptances.ToString(),
            ["collateralAcceptances"] = plan.Impact.CollateralAcceptances.ToString(),
            ["affectedStudents"] = plan.Impact.AffectedStudents.ToString()
        };
        await audit.RecordAsync(AuditAction.UserHardDeleted, "User", plan.User.Id.ToString(), metadata);
    }

    /// <summary>
    ///     Takes the user out of the Forgejo teachers team of every course that outlives them
    /// </summary>
    private async ValueTask RemoveFromSurvivingTeacherTeamsAsync(User user)
    {
        foreach (string org in await uow.CourseRepository.GetCoTaughtForgejoOrgsAsync(user.Id))
        {
            OneOf<ForgejoTeam, NotFound, ForgejoError> team =
                await forgejo.GetTeamAsync(org, ForgejoNaming.TeachersTeamName);
            long? teamId = team.Match<long?>(found => found.Id, notFound => null, error => null);
            if (teamId is null)
            {
                logger.LogWarning("Could not resolve the teachers team of {Org} while deleting {StudentId}",
                                  org, user.StudentId);

                continue;
            }

            OneOf<Success, ForgejoError> removed =
                await forgejo.RemoveTeamMemberAsync(teamId.Value, user.StudentId);
            if (removed.Failure is { } removeFailed)
            {
                logger.LogWarning("Removing {StudentId} from the teachers team of {Org} failed: {Reason}",
                                  user.StudentId, org, removeFailed.Reason);
            }
        }
    }

    private async ValueTask DeactivateForgejoAccountAsync(string studentId)
    {
        // Forgejo has no user-delete in the client, and its accounts own the repositories' history, so the account
        // is disabled rather than removed
        OneOf<Success, ForgejoError> result =
            await forgejo.SetUserActiveAsync(studentId, active: false, forgejoSettings.Value.AuthSourceId);
        if (result.Failure is { } failed)
        {
            logger.LogWarning("Deactivating the Forgejo account of {StudentId} failed: {Reason}",
                              studentId, failed.Reason);
        }
    }

    /// <param name="OwnedAssignmentIds">
    ///     Assignments this user owns inside courses that survive them; the ones inside courses that are also going
    ///     away are deleted with their course
    /// </param>
    /// <param name="OwnAcceptances">
    ///     This user's own submissions; other students' submissions are deleted with the assignment they belong to
    /// </param>
    private sealed record DeletionPlan(
        User User,
        IReadOnlyCollection<long> RosterIds,
        IReadOnlyCollection<long> CourseIds,
        IReadOnlyCollection<long> OwnedAssignmentIds,
        IReadOnlyCollection<AcceptanceRepoRef> OwnAcceptances,
        UserDeletionImpact Impact);
}
