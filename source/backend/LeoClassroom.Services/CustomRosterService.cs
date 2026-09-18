using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

public interface ICustomRosterService
{
    public ValueTask<IReadOnlyCollection<RosterOverview>> ListAsync();
    public ValueTask<OneOf<IReadOnlyCollection<UserSummary>, NotFound>> ListMembersAsync(long id);
    public ValueTask<OneOf<Success<Roster>, NotFound>> CreateAsync(string title);
    public ValueTask<OneOf<Success<Roster>, NotFound, Forbidden>> RenameAsync(long id, string title);
    public ValueTask<OneOf<Success, NotFound, Forbidden, ICustomRosterService.InUse>> DeleteAsync(long id);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> AddMemberAsync(long rosterId, long userId);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> RemoveMemberAsync(long rosterId, long userId);

    public readonly record struct InUse(int CourseCount);
}

internal sealed class CustomRosterService(
    IUnitOfWork uow, ICurrentUser currentUser, ILogger<CustomRosterService> logger) : ICustomRosterService
{
    public async ValueTask<IReadOnlyCollection<RosterOverview>> ListAsync() =>
        await uow.RosterRepository.GetOverviewsAsync();

    public async ValueTask<OneOf<IReadOnlyCollection<UserSummary>, NotFound>> ListMembersAsync(long id)
    {
        IReadOnlyCollection<UserSummary>? members = await uow.RosterRepository.GetMembersAsync(id);

        return members is null ? new NotFound() : OneOf<IReadOnlyCollection<UserSummary>, NotFound>.FromT0(members);
    }

    public async ValueTask<OneOf<Success<Roster>, NotFound>> CreateAsync(string title)
    {
        long? ownerId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (ownerId is null)
        {
            logger.LogWarning("Acting user {StudentId} has no local account to own a roster", currentUser.StudentId);

            return new NotFound();
        }

        var roster = new Roster { Name = title, Kind = RosterKind.Custom, OwnerId = ownerId.Value };
        uow.RosterRepository.Add(roster);
        await uow.SaveChangesAsync();
        logger.LogInformation("Created custom roster {RosterId} owned by {StudentId}", roster.Id, currentUser.StudentId);

        return new Success<Roster>(roster);
    }

    public async ValueTask<OneOf<Success<Roster>, NotFound, Forbidden>> RenameAsync(long id, string title)
    {
        Roster? roster = await uow.RosterRepository.GetTrackedWithMembersAsync(id);
        if (roster is null)
        {
            return new NotFound();
        }

        OneOf<Success, Forbidden> gate = await GateEditAsync(roster);
        if (gate.Failure is { } forbidden)
        {
            return forbidden;
        }

        roster.Name = title;
        await uow.SaveChangesAsync();

        return new Success<Roster>(roster);
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden, ICustomRosterService.InUse>> DeleteAsync(long id)
    {
        Roster? roster = await uow.RosterRepository.GetTrackedWithMembersAsync(id);
        if (roster is null)
        {
            return new NotFound();
        }

        OneOf<Success, Forbidden> gate = await GateEditAsync(roster);
        if (gate.Failure is { } forbidden)
        {
            return forbidden;
        }

        int blocking = await uow.RosterRepository.CountCoursesUsingAsync(id);
        if (blocking > 0)
        {
            logger.LogWarning("Refused delete of roster {RosterId}: {Count} courses depend on it", id, blocking);

            return new ICustomRosterService.InUse(blocking);
        }

        await uow.RosterRepository.DeleteByIdAsync(id);
        await uow.SaveChangesAsync();

        return new Success();
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> AddMemberAsync(long rosterId, long userId)
    {
        Roster? roster = await uow.RosterRepository.GetTrackedWithMembersAsync(rosterId);
        if (roster is null)
        {
            return new NotFound();
        }

        OneOf<Success, Forbidden> gate = await GateEditAsync(roster);
        if (gate.Failure is { } forbidden)
        {
            return forbidden;
        }

        User? user = await uow.UserRepository.GetTrackedByIdAsync(userId);
        if (user is null || user.State == UserState.SoftDeleted)
        {
            return new NotFound();
        }

        if (roster.Members.All(m => m.Id != userId))
        {
            roster.Members.Add(user);
            await uow.SaveChangesAsync();
        }

        return new Success();
    }

    public async ValueTask<OneOf<Success, NotFound, Forbidden>> RemoveMemberAsync(long rosterId, long userId)
    {
        Roster? roster = await uow.RosterRepository.GetTrackedWithMembersAsync(rosterId);
        if (roster is null)
        {
            return new NotFound();
        }

        OneOf<Success, Forbidden> gate = await GateEditAsync(roster);
        if (gate.Failure is { } forbidden)
        {
            return forbidden;
        }

        User? member = roster.Members.FirstOrDefault(m => m.Id == userId);
        if (member is not null)
        {
            roster.Members.Remove(member);
            await uow.SaveChangesAsync();
        }

        return new Success();
    }

    private async ValueTask<OneOf<Success, Forbidden>> GateEditAsync(Roster roster)
    {
        if (roster.Kind == RosterKind.Auto)
        {
            return new Forbidden();
        }

        if (currentUser.Roles.Contains(Role.Admin))
        {
            return new Success();
        }

        long? actingId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);

        return actingId is not null && roster.OwnerId == actingId ? new Success() : new Forbidden();
    }
}
