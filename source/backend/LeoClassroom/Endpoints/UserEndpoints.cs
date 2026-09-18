using LeoClassroom.Auth;
using LeoClassroom.Services;
using LeoClassroom.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints of the user directory
/// </summary>
public static class UserEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapUserEndpoints()
        {
            var users = app.MapGroup("/api/users")
                           .WithTags("Users")
                           .RequireAuthorization(AuthPolicies.RequireTeacher);

            users.MapGet("/search", SearchUsersAsync);
        }
    }

    private static async ValueTask<Ok<IReadOnlyCollection<UserSummary>>> SearchUsersAsync(
        [FromQuery] string? q, [FromQuery] Role? role, [FromServices] IUserDirectoryService directory) =>
        TypedResults.Ok(await directory.SearchAsync(q ?? string.Empty, role));
}
