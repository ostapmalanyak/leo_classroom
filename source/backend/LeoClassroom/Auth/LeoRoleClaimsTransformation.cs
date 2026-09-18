using System.Security.Claims;
using LeoClassroom.Shared;
using Microsoft.AspNetCore.Authentication;

namespace LeoClassroom.Auth;

/// <summary>
///     Supplies only the roles derived from the LDAP DN and configured administrator list
/// </summary>
internal sealed class LeoRoleClaimsTransformation(IReadOnlySet<string> adminUsers) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return Task.FromResult(principal);
        }

        // Incoming role claims must not bypass derivation or disagree with CurrentUser's ownership checks.
        // Clone identities so authentication's original principal is unchanged, even on repeated transforms.
        var transformed = new ClaimsPrincipal(principal.Identities.Select(identity =>
        {
            var clone = new ClaimsIdentity(identity);
            foreach (Claim claim in clone.FindAll(clone.RoleClaimType).ToArray())
            {
                clone.RemoveClaim(claim);
            }

            return clone;
        }));
        ClaimsIdentity target = transformed.Identities.First(identity => identity.IsAuthenticated);
        foreach (Role role in AuthClaims.ReadRoles(principal, adminUsers))
        {
            target.AddClaim(new Claim(target.RoleClaimType, role.ToString().ToLowerInvariant()));
        }

        return Task.FromResult(transformed);
    }
}
