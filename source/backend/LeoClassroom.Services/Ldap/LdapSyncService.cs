using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Ldap;

public readonly record struct SyncOutcome(
    int Created, int Updated, int SoftDeleted, int Reactivated, bool DestructivePassSkipped,
    int? LdapExitCode = null, int? LdapResultCode = null, string? LdapError = null);

public interface ILdapSyncService
{
    public ValueTask<SyncOutcome> RunAsync();
}

internal sealed class LdapSyncService(
    ILdapDirectory directory,
    IUnitOfWork uow,
    IForgejoClient forgejo,
    IClock clock,
    IOptions<LdapSettings> ldapSettings,
    IOptions<ForgejoSettings> forgejoSettings,
    IUserProvisioningCache provisioningCache,
    IAuditLog audit,
    ILogger<LdapSyncService> logger) : ILdapSyncService
{
    private const string StudentRosterKey = "@role:Student";
    private const string TeacherRosterKey = "@role:Teacher";

    public async ValueTask<SyncOutcome> RunAsync()
    {
        OneOf<IReadOnlyCollection<LdapPerson>, LdapError> read = await directory.SearchPeopleAsync();

        return await read.Match(
            people => SyncAsync(people),
            error =>
            {
                logger.LogWarning(
                    "LDAP read failed with ldapsearch exit code {ExitCode} and LDAP result code {ResultCode} " +
                    "for {BaseDn} ({Reason}); skipping the entire sync",
                    error.ExitCode, error.ResultCode, error.BaseDn, error.Reason);

                return ValueTask.FromResult(new SyncOutcome(
                    0, 0, 0, 0, DestructivePassSkipped: true,
                    LdapExitCode: error.ExitCode, LdapResultCode: error.ResultCode, LdapError: error.Reason));
            });
    }

    private async ValueTask<SyncOutcome> SyncAsync(IReadOnlyCollection<LdapPerson> people)
    {
        bool healthy = people.Count > 0;

        IReadOnlyCollection<User> existingUsers = await uow.UserRepository.GetAllTrackedAsync();
        Dictionary<string, User> usersByStudentId = existingUsers.ToDictionary(u => u.StudentId);
        HashSet<string> activeBefore = [.. existingUsers.Where(u => u.State == UserState.Active).Select(u => u.StudentId)];

        Instant now = clock.GetCurrentInstant();
        int created = 0;
        int updated = 0;
        int reactivated = 0;

        foreach (LdapPerson person in people)
        {
            if (usersByStudentId.TryGetValue(person.StudentId, out User? user))
            {
                if (user.State == UserState.SoftDeleted)
                {
                    reactivated++;
                    await SetForgejoActiveAsync(person.StudentId, active: true);
                    await audit.RecordAsync(AuditAction.UserReactivated, "User", user.Id.ToString(),
                                            new Dictionary<string, string> { ["studentId"] = person.StudentId });
                }
                else
                {
                    updated++;
                }
            }
            else
            {
                user = new User { StudentId = person.StudentId, FirstName = string.Empty, LastName = string.Empty };
                uow.UserRepository.Add(user);
                usersByStudentId[person.StudentId] = user;
                created++;
            }

            ApplyPerson(user, person, now);
            await EnsureForgejoUserAsync(person);
        }

        (int softDeleted, bool skipped) = await ApplyGuardedSoftDeleteAsync(healthy, activeBefore, people, usersByStudentId);
        if (!skipped)
        {
            await ReconcileAutoRostersAsync(people, usersByStudentId);
        }

        await uow.SaveChangesAsync();

        // the sync is the one place that changes User.State in bulk; refreshing from what it just wrote keeps the
        // cache warm and drops exactly the accounts it soft-deleted
        provisioningCache.Refresh(usersByStudentId.Values.Select(AsProvisioned).ToList());

        return new SyncOutcome(created, updated, softDeleted, reactivated, DestructivePassSkipped: skipped);
    }

    private static ProvisionedUser AsProvisioned(User user) =>
        new(user.StudentId, user.FirstName, user.LastName, user.Email, user.Role, user.Class, user.State);

    private static void ApplyPerson(User user, LdapPerson person, Instant now)
    {
        user.FirstName = person.FirstName;
        user.LastName = person.LastName;
        user.Email = person.Email;
        user.Class = person.Class;
        user.Role = person.Role;
        user.State = UserState.Active;
        user.LdapLastSeen = now;
    }

    private async ValueTask ReconcileAutoRostersAsync(IReadOnlyCollection<LdapPerson> people,
                                                      Dictionary<string, User> usersByStudentId)
    {
        Dictionary<string, List<User>> desiredMembers = people
            .Where(p => p.Role == Role.Student && !string.IsNullOrWhiteSpace(p.Class))
            .GroupBy(p => p.Class!)
            .ToDictionary(g => g.Key, g => g.Select(p => usersByStudentId[p.StudentId]).ToList());
        desiredMembers[StudentRosterKey] = people
            .Where(p => p.Role == Role.Student)
            .Select(p => usersByStudentId[p.StudentId])
            .ToList();
        desiredMembers[TeacherRosterKey] = people
            .Where(p => p.Role == Role.Teacher)
            .Select(p => usersByStudentId[p.StudentId])
            .ToList();

        IReadOnlyCollection<Roster> autoRosters = await uow.RosterRepository.GetAllTrackedAutoRostersAsync();
        Dictionary<string, Roster> rostersByClass = autoRosters
            .Where(r => r.ClassKey is not null)
            .ToDictionary(r => r.ClassKey!);

        foreach ((string key, List<User> members) in desiredMembers)
        {
            if (!rostersByClass.TryGetValue(key, out Roster? roster))
            {
                string name = key switch
                {
                    StudentRosterKey => "Students",
                    TeacherRosterKey => "Teachers",
                    _ => key
                };
                roster = new Roster { Name = name, Kind = RosterKind.Auto, ClassKey = key };
                uow.RosterRepository.Add(roster);
                rostersByClass[key] = roster;
            }

            SetMembership(roster, members);
        }

        foreach (Roster roster in autoRosters.Where(r => r.ClassKey is not null
                                                         && !desiredMembers.ContainsKey(r.ClassKey)))
        {
            roster.Members.Clear();
        }
    }

    private static void SetMembership(Roster roster, List<User> desiredMembers)
    {
        HashSet<string> desiredStudentIds = [.. desiredMembers.Select(m => m.StudentId)];
        foreach (User existing in roster.Members.Where(m => !desiredStudentIds.Contains(m.StudentId)).ToList())
        {
            roster.Members.Remove(existing);
        }

        HashSet<string> presentStudentIds = [.. roster.Members.Select(m => m.StudentId)];
        foreach (User member in desiredMembers.Where(m => !presentStudentIds.Contains(m.StudentId)))
        {
            roster.Members.Add(member);
        }
    }

    private async ValueTask<(int Count, bool Skipped)> ApplyGuardedSoftDeleteAsync(
        bool healthy, HashSet<string> activeBefore, IReadOnlyCollection<LdapPerson> people,
        Dictionary<string, User> usersByStudentId)
    {
        HashSet<string> present = [.. people.Select(p => p.StudentId)];
        List<string> disappeared = [.. activeBefore.Where(number => !present.Contains(number))];

        int threshold = ldapSettings.Value.SoftDeleteThreshold;
        if (!healthy || disappeared.Count > threshold)
        {
            logger.LogWarning(
                "Skipping LDAP soft-delete pass: healthy={Healthy}, disappeared={Count}, threshold={Threshold}",
                healthy, disappeared.Count, threshold);

            return (0, true);
        }

        foreach (string number in disappeared)
        {
            User user = usersByStudentId[number];
            user.State = UserState.SoftDeleted;
            await SetForgejoActiveAsync(number, active: false);
            await audit.RecordAsync(AuditAction.UserSoftDeleted, "User", user.Id.ToString(),
                                    new Dictionary<string, string> { ["studentId"] = number });
        }

        return (disappeared.Count, false);
    }

    private async ValueTask EnsureForgejoUserAsync(LdapPerson person)
    {
        OneOf<Success, ForgejoError> result =
            await forgejo.EnsureUserAsync(person.StudentId, person.Email, forgejoSettings.Value.AuthSourceId);
        if (result.Failure is { } failed)
        {
            logger.LogWarning("Could not ensure Forgejo account for {StudentId}: {Reason}",
                              person.StudentId, failed.Reason);
        }
    }

    private async ValueTask SetForgejoActiveAsync(string studentId, bool active)
    {
        OneOf<Success, ForgejoError> result =
            await forgejo.SetUserActiveAsync(studentId, active, forgejoSettings.Value.AuthSourceId);
        if (result.Failure is { } failed)
        {
            logger.LogWarning("Could not set Forgejo account {StudentId} active={Active}: {Reason}",
                              studentId, active, failed.Reason);
        }
    }
}
