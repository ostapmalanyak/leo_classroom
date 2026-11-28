using LeoClassroom.Shared;

namespace LeoClassroom.Persistence.Model;

public class User
{
    public long Id { get; set; }
    public required string StudentId { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Email { get; set; }
    public string? Class { get; set; }
    public Role Role { get; set; }
    public UserState State { get; set; }
    public Instant? LdapLastSeen { get; set; }

    /// <summary>
    ///     When this user last had a Forgejo git password issued, or null if never
    /// </summary>
    /// <remarks>
    ///     Only the timestamp is kept. The password itself is shown once and never stored, so a forgotten one
    ///     is replaced rather than recovered.
    /// </remarks>
    public Instant? GitCredentialIssuedAt { get; set; }

    /// <summary>
    ///     When this user last ran the one-time git login setup, or null if never
    /// </summary>
    public Instant? GitSetupConfiguredAt { get; set; }

    /// <summary>
    ///     When a git operation proved that the stored credential works, or null while it is still unproven
    /// </summary>
    public Instant? GitSetupAuthorizedAt { get; set; }

    /// <summary>
    ///     The opaque token in this user's setup URL. Random, unique and written once: it only ever marks the
    ///     user as configured, so it grants nothing else.
    /// </summary>
    public string? GitSetupToken { get; set; }

    public ICollection<Roster> Rosters { get; set; } = [];
}
