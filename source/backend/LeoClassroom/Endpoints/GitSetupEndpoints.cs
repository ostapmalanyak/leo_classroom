using LeoClassroom.Services.GitSetup;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The caller's one-time git login setup: its state, and the callback that records the commands were run
/// </summary>
/// <remarks>
///     The page shows the commands themselves instead of serving a script, so nothing here is fetched by a shell.
///     The only unauthenticated route is the done callback, guarded by the per-user token in the path, which is
///     random and marks nothing but "this user ran the commands". Neither route logs the token.
/// </remarks>
public static class GitSetupEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapGitSetupEndpoints()
        {
            app.MapGet("/api/me/git-setup", GetStatusAsync)
               .WithTags("Me")
               .RequireAuthorization();

            app.MapPost("/api/git-setup/done/{token}", MarkDoneAsync)
               .WithTags("Git setup")
               .AllowAnonymous();
        }
    }

    private static async ValueTask<Results<Ok<GitSetupDto>, NotFound>> GetStatusAsync(
        [FromServices] IGitSetupService setup)
    {
        GitSetupStatus? status = await setup.GetStatusAsync();

        return status is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(GitSetupDto.From(status));
    }

    private static async ValueTask<NoContent> MarkDoneAsync(
        [FromRoute] string token, [FromServices] IGitSetupService setup)
    {
        // Idempotent and always 204: running the commands twice is a supported thing to do.
        await setup.MarkConfiguredAsync(token);

        return TypedResults.NoContent();
    }
}

/// <param name="Token">The opaque part of this user's setup URL</param>
/// <param name="Host">The Forgejo host and port, the key the git config is written under</param>
/// <param name="ClientId">The Forgejo OAuth2 application id the git config names</param>
public sealed record GitSetupDto(
    bool Configured, bool Authorized, string Token, string Host, string ClientId)
{
    public static GitSetupDto From(GitSetupStatus status) =>
        new(status.Configured, status.Authorized, status.Token, status.Host, status.ClientId);
}
