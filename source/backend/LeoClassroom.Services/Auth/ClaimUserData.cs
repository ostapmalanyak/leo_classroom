using LeoClassroom.Shared;

namespace LeoClassroom.Services.Auth;

/// <param name="Role">
///     The role the token asserts, or null when it asserts none - in which case an existing user keeps the role
///     they already have rather than being silently demoted
/// </param>
public sealed record ClaimUserData(
    string StudentId,
    string FirstName,
    string LastName,
    string? Email,
    Role? Role,
    string? Class);
