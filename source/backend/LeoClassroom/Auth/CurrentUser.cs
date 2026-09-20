using System.Security.Claims;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Auth;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor, IOptions<KeycloakSettings> keycloak)
    : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public string StudentId =>
        Principal is null ? string.Empty : AuthClaims.ReadStudentId(Principal) ?? string.Empty;

    public IReadOnlySet<Role> Roles =>
        Principal is null ? new HashSet<Role>() : AuthClaims.ReadRoles(Principal, AdminUsers, TeacherUsers);

    public string? Class => Principal?.FindFirstValue(AuthClaims.ClassClaim);

    private HashSet<string> AdminUsers => new(keycloak.Value.AdminUsers, StringComparer.Ordinal);

    private HashSet<string> TeacherUsers => new(keycloak.Value.TeacherUsers, StringComparer.Ordinal);
}
