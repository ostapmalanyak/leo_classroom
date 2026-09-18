using System.Security.Claims;
using LeoClassroom.Shared;

namespace LeoClassroom.Auth;

/// <summary>
///     Reads identity and roles out of an HTL Leonding Keycloak token
/// </summary>
/// <remarks>
///     The shared realm does not carry a roles claim. A user's group is instead readable from their LDAP
///     distinguished name, which the realm puts in <c>ldap_entry_dn</c> - the same thing the school's own
///     <c>LeoAuth</c> samples key on. Administrators are not expressible at all in that scheme, so they are
///     configured by username instead (<c>Keycloak:AdminUsers</c>).
/// </remarks>
public static class AuthClaims
{
    /// <summary>
    ///     The Keycloak claim carrying the IF number, which is this application's user identity
    /// </summary>
    public const string StudentIdClaim = "preferred_username";

    /// <summary>
    ///     The claim carrying the caller's LDAP distinguished name, from which their group is read
    /// </summary>
    public const string LdapEntryDnClaim = "ldap_entry_dn";

    public const string ClassClaim = "class";

    private const string StudentsOrgUnit = "Students";
    private const string TeachersOrgUnit = "Teachers";
    private const string TestUsersOrgUnit = "TestUsers";

    /// <summary>
    ///     Reads the IF number that identifies the caller
    /// </summary>
    /// <remarks>
    ///     Deliberately reads <see cref="StudentIdClaim" /> and nothing else. Falling back to
    ///     <see cref="ClaimTypes.Name" /> would be wrong: with inbound claim mapping enabled, Keycloak's
    ///     <c>name</c> claim (the display name) lands there, and every ownership check in the services layer
    ///     compares against this value.
    /// </remarks>
    /// <param name="principal">The authenticated caller</param>
    /// <returns>The IF number, or null when the token does not carry one</returns>
    public static string? ReadStudentId(ClaimsPrincipal principal)
    {
        string? studentId = principal.FindFirstValue(StudentIdClaim);

        return string.IsNullOrWhiteSpace(studentId) ? null : studentId;
    }

    /// <summary>
    ///     Reads the caller's roles
    /// </summary>
    /// <param name="principal">The authenticated caller</param>
    /// <param name="adminUsers">The usernames configured as administrators</param>
    public static IReadOnlySet<Role> ReadRoles(ClaimsPrincipal principal, IReadOnlySet<string> adminUsers)
    {
        var roles = new HashSet<Role>();

        string? distinguishedName = principal.FindFirstValue(LdapEntryDnClaim);
        if (distinguishedName is not null)
        {
            if (Contains(distinguishedName, TeachersOrgUnit))
            {
                roles.Add(Role.Teacher);
            }

            // test accounts get the least privileged role rather than a role of their own
            if (Contains(distinguishedName, StudentsOrgUnit) || Contains(distinguishedName, TestUsersOrgUnit))
            {
                roles.Add(Role.Student);
            }
        }

        if (ReadStudentId(principal) is { } studentId && adminUsers.Contains(studentId))
        {
            roles.Add(Role.Admin);
        }

        return roles;
    }

    /// <summary>
    ///     The highest role the token asserts, or null when it asserts none
    /// </summary>
    /// <remarks>
    ///     Null rather than <see cref="Role.Student" />: a token whose group cannot be read must not be able to
    ///     rewrite an existing teacher's or admin's stored role.
    /// </remarks>
    public static Role? PrimaryRole(IReadOnlySet<Role> roles)
    {
        if (roles.Contains(Role.Admin))
        {
            return Role.Admin;
        }

        if (roles.Contains(Role.Teacher))
        {
            return Role.Teacher;
        }

        return roles.Contains(Role.Student) ? Role.Student : null;
    }

    private static bool Contains(string distinguishedName, string organisationalUnit) =>
        LdapDistinguishedName.ContainsOrganisationalUnit(distinguishedName, organisationalUnit);
}
