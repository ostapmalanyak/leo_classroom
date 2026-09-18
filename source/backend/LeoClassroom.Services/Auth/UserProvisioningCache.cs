using System.Collections.Concurrent;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Auth;

/// <summary>
///     What the cache knows about a caller
/// </summary>
public enum ProvisioningState
{
    /// <summary>Never seen, or the stored row no longer matches the claims - has to be reconciled</summary>
    Unknown = 0,

    /// <summary>Provisioned and active; the request proceeds without touching the database</summary>
    Active = 1,

    /// <summary>Provisioned but soft-deleted; the request is refused without touching the database</summary>
    Inactive = 2
}

/// <summary>
///     Holds the stored state of every user the backend knows about, so that a request costs a dictionary lookup
///     instead of a database round-trip
/// </summary>
/// <remarks>
///     <para>
///         Provisioning is a once-per-user event: a caller is written to the database the first time they appear
///         and never needs writing again unless something about them actually changed. The cache therefore has no
///         expiry. It is filled from the user table at boot, grows as callers are provisioned, and is refreshed by
///         the LDAP sync from the state that sync just wrote.
///     </para>
///     <para>
///         Both answers are cached, not just the positive one: a disabled account presenting a still-valid token
///         is refused from memory, so repeated attempts cannot be used to put load on the database. Only a
///         genuinely unknown identity - or one whose claims diverge from what was stored - reaches provisioning.
///     </para>
///     <para>
///         This assumes a single backend instance, which this application already requires for other reasons (the
///         Quartz scheduler runs non-clustered against an in-memory job store). A second replica would not see the
///         first one's <see cref="Forget" />, so introducing one means giving this cache a real invalidation
///         channel.
///     </para>
/// </remarks>
public interface IUserProvisioningCache
{
    /// <summary>
    ///     Reports what is known about this caller, given the claims they are presenting
    /// </summary>
    public ProvisioningState Lookup(ClaimUserData data);

    /// <summary>
    ///     Records the stored state of a caller whose row matches these claims
    /// </summary>
    public void Remember(ClaimUserData data, ProvisioningState state);

    /// <summary>
    ///     Reports whether a refused attempt by this caller should be written to the audit log, and records that it
    ///     was
    /// </summary>
    /// <remarks>
    ///     Every attempt reaches the application log. The audit log is a database table, so it is written at most
    ///     once per <see cref="UserProvisioningCache.AuditThrottle" /> per account - enough for an administrator to
    ///     see that a disabled account is still trying, without letting that account choose how many rows the
    ///     backend writes.
    /// </remarks>
    public bool ShouldAuditRefusal(string studentId);

    /// <summary>
    ///     Drops a caller, so their next request is reconciled against the database again. Call this whenever a
    ///     user is deleted or otherwise changed outside <c>UserProvisioningService</c>.
    /// </summary>
    public void Forget(string studentId);

    /// <summary>
    ///     Records the given users exactly as they are currently stored
    /// </summary>
    public void Refresh(IEnumerable<ProvisionedUser> users);
}

internal sealed class UserProvisioningCache(IClock clock, ILogger<UserProvisioningCache> logger)
    : IUserProvisioningCache
{
    /// <summary>
    ///     How often a single disabled account can add a row to the audit log
    /// </summary>
    public static readonly Duration AuditThrottle = Duration.FromHours(1);

    /// <summary>
    ///     A sanity bound, not a working eviction policy. Entries only exist for identities Keycloak has issued a
    ///     valid token for, so passing this means something is wrong rather than that the school grew.
    /// </summary>
    private const int MaxEntries = 100_000;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public ProvisioningState Lookup(ClaimUserData data)
    {
        if (!_entries.TryGetValue(data.StudentId, out Entry entry) || entry.ClaimsHash != Fingerprint(data))
        {
            // never seen, or the stored row no longer matches the claims: reconcile now
            return ProvisioningState.Unknown;
        }

        return entry.State;
    }

    public void Remember(ClaimUserData data, ProvisioningState state)
    {
        if (state == ProvisioningState.Unknown)
        {
            Forget(data.StudentId);

            return;
        }

        if (_entries.Count >= MaxEntries && !_entries.ContainsKey(data.StudentId))
        {
            logger.LogError("Provisioning cache reached {MaxEntries} entries and was reset", MaxEntries);
            _entries.Clear();
        }

        _entries.AddOrUpdate(data.StudentId,
                             _ => new Entry(Fingerprint(data), state, LastAuditedRefusal: null),
                             (_, existing) => existing with { ClaimsHash = Fingerprint(data), State = state });
    }

    public bool ShouldAuditRefusal(string studentId)
    {
        if (!_entries.TryGetValue(studentId, out Entry entry))
        {
            // nothing to throttle against, so the caller is reconciling anyway and the refusal is worth recording
            return true;
        }

        Instant now = clock.GetCurrentInstant();
        if (entry.LastAuditedRefusal is { } last && now - last < AuditThrottle)
        {
            return false;
        }

        _entries.TryUpdate(studentId, entry with { LastAuditedRefusal = now }, entry);

        return true;
    }

    public void Forget(string studentId) => _entries.TryRemove(studentId, out _);

    public void Refresh(IEnumerable<ProvisionedUser> users)
    {
        int active = 0;
        int inactive = 0;
        foreach (ProvisionedUser user in users)
        {
            bool isActive = user.State == UserState.Active;
            Remember(new ClaimUserData(user.StudentId, user.FirstName, user.LastName, user.Email, user.Role,
                                       user.Class),
                     isActive ? ProvisioningState.Active : ProvisioningState.Inactive);

            if (isActive)
            {
                active++;
            }
            else
            {
                inactive++;
            }
        }

        logger.LogInformation("Provisioning cache refreshed: {Active} active and {Inactive} disabled users known",
                              active, inactive);
    }

    /// <summary>
    ///     Covers exactly the columns <c>UserProvisioningService</c> writes, so that a caller whose name, mail
    ///     address, role or class changed is reconciled and one whose data is unchanged never is
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A token asserting no role hashes differently from the role stored for that user, which costs that
    ///         caller one reconciliation - after which the entry matches and the next request is free again.
    ///     </para>
    ///     <para>
    ///         <c>Class</c> belongs here because provisioning writes it and joins the caller to the automatic
    ///         roster for it. Leaving it out meant a student who changed class was never reconciled: their stored
    ///         class stayed stale and they stayed in their old class's roster.
    ///     </para>
    /// </remarks>
    private static int Fingerprint(ClaimUserData data) =>
        HashCode.Combine(data.FirstName, data.LastName, data.Email, data.Role, data.Class);

    private readonly record struct Entry(int ClaimsHash, ProvisioningState State, Instant? LastAuditedRefusal);
}
