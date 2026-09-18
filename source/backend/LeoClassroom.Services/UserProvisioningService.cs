using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

public interface IUserProvisioningService
{
    public ValueTask<OneOf<Success, Forbidden>> EnsureUserAsync(ClaimUserData data);
}

internal sealed class UserProvisioningService(
    IUnitOfWork uow, IUserProvisioningCache cache, IAuditLog audit, ILogger<UserProvisioningService> logger)
    : IUserProvisioningService
{
    public async ValueTask<OneOf<Success, Forbidden>> EnsureUserAsync(ClaimUserData data)
    {
        // the common case: this caller's stored state is known and their claims are unchanged, so the database is
        // not touched at all - for a disabled account either, which is what keeps repeated attempts by one from
        // becoming load on the database
        switch (cache.Lookup(data))
        {
            case ProvisioningState.Active:
                return new Success();

            case ProvisioningState.Inactive:
                return await RefuseAsync(data, userId: null);
        }

        var existing = await uow.UserRepository.GetTrackedByStudentIdAsync(data.StudentId);

        if (existing is null)
        {
            var created = new User
            {
                StudentId = data.StudentId,
                FirstName = data.FirstName,
                LastName = data.LastName,
                Email = data.Email,
                Class = data.Class,
                // a first-time caller whose token asserts no role gets the least privileged one
                Role = data.Role ?? Role.Student,
                State = UserState.Active
            };
            uow.UserRepository.Add(created);
            await EnsureClassRosterMembershipAsync(created);
            await uow.SaveChangesAsync();
            logger.LogInformation("Provisioned new user {StudentId} from claims", data.StudentId);
            cache.Remember(data, ProvisioningState.Active);

            return new Success();
        }

        if (existing.State == UserState.SoftDeleted)
        {
            cache.Remember(data, ProvisioningState.Inactive);

            return await RefuseAsync(data, existing.Id);
        }

        existing.FirstName = data.FirstName;
        existing.LastName = data.LastName;
        existing.Email = data.Email;

        // The nightly LDAP sync is the authority on a user's class, but it only runs where the directory is
        // configured. Until then the token's claim is the only source there is, and dropping it left every
        // user class-less: no class on their account, and nothing to group them by.
        if (data.Class is { Length: > 0 })
        {
            existing.Class = data.Class;
        }

        // only an asserted role updates the stored one - a token that carries no roles claim (an unmapped client,
        // a misconfigured scope) must not demote a teacher or admin to student
        if (data.Role is { } assertedRole)
        {
            existing.Role = assertedRole;
        }
        else
        {
            logger.LogWarning("Token for {StudentId} asserted no role; keeping the stored role {Role}",
                              data.StudentId, existing.Role);
        }

        await EnsureClassRosterMembershipAsync(existing);
        await uow.SaveChangesAsync();
        cache.Remember(data, ProvisioningState.Active);

        return new Success();
    }

    /// <summary>
    ///     Puts a user in the automatic roster for their class, creating that roster the first time
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The automatic rosters are normally reconciled by the nightly LDAP sync, which knows the whole
    ///         school at once. Without directory credentials that sync never runs, and a deployment ends up
    ///         with no rosters at all - so a teacher cannot create a course, because a course needs one.
    ///     </para>
    ///     <para>
    ///         A user who has signed in at least once is a user the token has told us about, class included.
    ///         That is enough to build the same rosters incrementally: each class roster starts empty and
    ///         fills as its students sign in. It is not a substitute for the sync - a student who has never
    ///         signed in is invisible, and nobody is ever removed here - but it turns an empty deployment
    ///         into a usable one on the first login.
    ///     </para>
    ///     <para>
    ///         When the sync is configured later it takes over these same rosters, matched on
    ///         <see cref="Roster.ClassKey" />, and its membership is authoritative from then on.
    ///     </para>
    /// </remarks>
    /// <remarks>
    ///     Leaves saving to the caller. Provisioning runs from <c>UserProvisioningMiddleware</c>, which sits
    ///     before the endpoints and therefore before any <c>BeginTransactionAsync</c> - unlike a service
    ///     called from an endpoint, where repeated <c>SaveChangesAsync</c> calls all enlist in the
    ///     transaction the endpoint opened. Here each save would commit on its own, so a failure between two
    ///     of them would leave a user carrying a class but not the membership that goes with it.
    /// </remarks>
    private async ValueTask EnsureClassRosterMembershipAsync(User user)
    {
        if (user.Class is not { Length: > 0 } classKey)
        {
            return;
        }

        Roster? roster = await uow.RosterRepository.GetTrackedAutoRosterByClassAsync(classKey);
        if (roster is null)
        {
            roster = new Roster { Name = classKey, Kind = RosterKind.Auto, ClassKey = classKey };
            uow.RosterRepository.Add(roster);
            logger.LogInformation("Created the automatic roster for class {Class} from a sign-in", classKey);
        }
        else if (roster.Members.Any(member => member.Id == user.Id))
        {
            return;
        }

        roster.Members.Add(user);
        logger.LogInformation("Added {StudentId} to the automatic roster for class {Class}",
                              user.StudentId, classKey);
    }

    /// <summary>
    ///     Refuses a disabled account, recording the attempt
    /// </summary>
    /// <remarks>
    ///     Every attempt is logged. The audit log is a database table, so it is written at most once per account
    ///     per throttle window - otherwise a disabled account with a still-valid token would decide how many rows
    ///     the backend writes.
    /// </remarks>
    private async ValueTask<OneOf<Success, Forbidden>> RefuseAsync(ClaimUserData data, long? userId)
    {
        logger.LogWarning("Soft-deleted user {StudentId} attempted to authenticate and was rejected",
                          data.StudentId);

        if (cache.ShouldAuditRefusal(data.StudentId))
        {
            await audit.RecordAsync(AuditAction.RejectedSoftDeletedLogin, "User", userId?.ToString(),
                                    new Dictionary<string, string> { ["studentId"] = data.StudentId });
        }

        return new Forbidden();
    }
}
