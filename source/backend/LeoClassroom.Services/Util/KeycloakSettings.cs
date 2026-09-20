namespace LeoClassroom.Services.Util;

public sealed class KeycloakSettings
{
    public const string SectionKey = "Keycloak";

    public required string Authority { get; init; }

    /// <summary>
    ///     The expected <c>aud</c> claim, checked only when <see cref="ValidateAudience" /> is on
    /// </summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    ///     Whether the <c>aud</c> claim is verified
    /// </summary>
    /// <remarks>
    ///     Off by default: the HTL Leonding realm does not set an audience on its access tokens, so requiring
    ///     one rejects every request. Turn it on for a Keycloak that does issue it. The issuer, signature and
    ///     lifetime are always validated regardless.
    /// </remarks>
    public bool ValidateAudience { get; init; }

    public bool RequireHttpsMetadata { get; init; } = true;

    /// <summary>
    ///     The usernames (IF numbers) that act as administrators
    /// </summary>
    /// <remarks>
    ///     The shared school realm expresses only students and teachers - there is no administrator role to
    ///     read out of a token - so this application names its own, by <c>preferred_username</c>.
    /// </remarks>
    public IReadOnlyList<string> AdminUsers { get; init; } = [];

    /// <summary>
    ///     The usernames (IF numbers) that act as teachers, regardless of their LDAP distinguished name
    /// </summary>
    public IReadOnlyList<string> TeacherUsers { get; init; } = [];
}
