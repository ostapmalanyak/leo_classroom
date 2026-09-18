using LeoClassroom.Shared;

namespace LeoClassroom.Services.Auth;

public interface ICurrentUser
{
    public bool IsAuthenticated { get; }
    public string StudentId { get; }
    public IReadOnlySet<Role> Roles { get; }
    public string? Class { get; }

    public bool IsInRole(Role role) => Roles.Contains(role);
}
