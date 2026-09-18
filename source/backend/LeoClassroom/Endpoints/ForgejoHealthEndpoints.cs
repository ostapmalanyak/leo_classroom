using LeoClassroom.Auth;
using LeoClassroom.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace LeoClassroom.Endpoints;

/// <summary>
///     "Can this application still drive Forgejo?", on demand, for an administrator
/// </summary>
public static class ForgejoHealthEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapForgejoHealthEndpoints()
        {
            var forgejo = app.MapGroup("/api/admin/forgejo")
                             .WithTags("Admin")
                             .RequireAuthorization(AuthPolicies.RequireAdmin);

            forgejo.MapGet("/health", GetForgejoHealthAsync);
        }
    }

    private static async ValueTask<Ok<ForgejoHealthReport>> GetForgejoHealthAsync(
        [FromServices] IForgejoDiagnosticsService diagnostics) =>
        TypedResults.Ok(await diagnostics.RunAsync());
}
