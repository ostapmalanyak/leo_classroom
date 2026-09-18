using LeoClassroom.Persistence.Util;
using LeoClassroom.Services;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ServiceNotFound = OneOf.Types.NotFound;
using ResetResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Ok<LeoClassroom.Endpoints.IssuedGitCredentialDto>,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The caller's own account: who the backend thinks they are, and their git password
/// </summary>
/// <remarks>
///     <para>
///         Available to every authenticated user rather than to a single role: students push submissions and
///         teachers push starter material, and both do it over git.
///     </para>
///     <para>
///         The roles here are the ones the backend actually enforces with. The SPA reads them from this
///         endpoint instead of parsing the token, because the school realm carries no roles claim - they are
///         derived from <c>ldap_entry_dn</c> and from configuration, and configuration is not in the token.
///     </para>
/// </remarks>
public static class MeEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapMeEndpoints()
        {
            var me = app.MapGroup("/api/me")
                        .WithTags("Me")
                        .RequireAuthorization();

            me.MapGet("/", GetMeAsync);
            me.MapPost("/git-credential/reset", ResetGitCredentialAsync);
        }
    }

    private static async ValueTask<Results<Ok<MeDto>, NotFound>> GetMeAsync(
        [FromServices] IGitCredentialService credentials, [FromServices] ICurrentUser currentUser)
    {
        var result = await credentials.GetStatusAsync();

        return result.ToOk(status => MeDto.From(currentUser, status));
    }

    private static async ValueTask<ResetResult> ResetGitCredentialAsync(
        [FromServices] IGitCredentialService credentials, [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await credentials.ResetAsync();

        return await result.Match(OnIssuedAsync, OnNotFoundAsync, OnUpstreamFailureAsync);

        async ValueTask<ResetResult> OnIssuedAsync(IssuedGitCredential issued)
        {
            await transaction.CommitAsync();

            return TypedResults.Ok(IssuedGitCredentialDto.FromIssued(issued));
        }

        // left uncommitted on purpose - scoped unit-of-work disposal rolls the transaction back
        static ValueTask<ResetResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<ResetResult>(TypedResults.NotFound());

        static ValueTask<ResetResult> OnUpstreamFailureAsync(ForgejoError error) =>
            ValueTask.FromResult<ResetResult>(ApiResults.BadGateway(error.Reason));
    }
}

/// <param name="Roles">The roles the backend enforces with, not what the token claims</param>
/// <param name="GitCredentialIssuedAt">When a git password was last issued, or null if never</param>
/// <param name="GitCredentialManaged">False when Forgejo passwords come from an external login source</param>
public sealed record MeDto(
    string StudentId,
    string? Class,
    IReadOnlyCollection<Role> Roles,
    Instant? GitCredentialIssuedAt,
    bool GitCredentialManaged)
{
    public static MeDto From(ICurrentUser currentUser, GitCredentialStatus status) =>
        new(status.Username, currentUser.Class, [.. currentUser.Roles], status.IssuedAt, status.Managed);
}

/// <param name="Password">
///     Returned exactly once. It is not stored, so a user who loses it generates a new one rather than
///     retrieving this one.
/// </param>
public sealed record IssuedGitCredentialDto(string Username, string Password, Instant IssuedAt)
{
    public static IssuedGitCredentialDto FromIssued(IssuedGitCredential issued) =>
        new(issued.Username, issued.Password, issued.IssuedAt);
}
