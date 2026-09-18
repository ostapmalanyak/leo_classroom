using System.Security.Claims;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;
using LeoClassroom.Services;
using Microsoft.AspNetCore.Authorization;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Auth;

/// <summary>
///     Materialises the caller as a <c>User</c> row on their first authenticated request and refuses the request
///     when that account has been soft-deleted
/// </summary>
/// <remarks>
///     Registered after authorization and before the endpoints: the authorization policies are claim-based and need
///     no user row, so running last means a request that is about to be refused - unmatched route, anonymous
///     endpoint, or failed policy - never causes a database write.
/// </remarks>
public sealed class UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IServiceScopeFactory scopeFactory,
                                  IUserProvisioningCache cache, IOptions<KeycloakSettings> keycloak)
    {
        if (!RequiresProvisioning(context))
        {
            await next(context);

            return;
        }

        var data = BuildClaimUserData(context.User, new HashSet<string>(keycloak.Value.AdminUsers, StringComparer.Ordinal));
        if (data is null)
        {
            logger.LogWarning("Authenticated principal is missing the preferred_username (IF number) claim");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        // The cache answers for a caller whose stored row already matches their claims, which is nearly every
        // request. Asking here rather than only inside the service keeps that path free of a database
        // connection entirely: without it, opening a transaction below would turn a dictionary lookup into a
        // BEGIN and a COMMIT on every authenticated request.
        if (cache.Lookup(data) == ProvisioningState.Active)
        {
            await next(context);

            return;
        }

        OneOf<Success, Forbidden> result = await ProvisionAsync(scopeFactory, cache, data);
        if (result.Failure is not null)
        {
            logger.LogInformation("Refused a request from the soft-deleted account {StudentId}", data.StudentId);

            // same RFC 9457 shape the endpoints and the exception handler use
            await Results.Problem("This account is disabled.", statusCode: StatusCodes.Status403Forbidden,
                                  title: "Forbidden")
                         .ExecuteAsync(context);

            return;
        }

        await next(context);
    }

    /// <summary>
    ///     Runs provisioning in a scope and a transaction of its own
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This middleware sits before the endpoints, so there is no transaction to join - an endpoint opens
    ///         its own further down the pipeline, and provisioning has already finished by then. Its writes are a
    ///         unit in their own right: a user row, that user's membership in the automatic roster for their
    ///         class, and the audit row recording a refused sign-in. Left to <c>SaveChangesAsync</c>'s implicit
    ///         transactions those would commit one at a time.
    ///     </para>
    ///     <para>
    ///         The scope is its own for the same reason: provisioning commits independently of whatever the
    ///         request goes on to do, so it should not share a change tracker with it.
    ///     </para>
    /// </remarks>
    private static async Task<OneOf<Success, Forbidden>> ProvisionAsync(
        IServiceScopeFactory scopeFactory, IUserProvisioningCache cache, ClaimUserData data)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var provisioning = scope.ServiceProvider.GetRequiredService<IUserProvisioningService>();
        var transaction = scope.ServiceProvider.GetRequiredService<ITransactionProvider>();

        try
        {
            return await transaction.ExecuteAsync(async () => await provisioning.EnsureUserAsync(data));
        }
        catch
        {
            // the service records the caller in the cache before the commit, so a rolled-back attempt would
            // otherwise leave the cache asserting a row that does not exist - and the next request would trust
            // it and skip provisioning for good
            cache.Forget(data.StudentId);

            throw;
        }
    }

    private static bool RequiresProvisioning(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var endpoint = context.GetEndpoint();

        return endpoint is not null && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
    }

    private static ClaimUserData? BuildClaimUserData(ClaimsPrincipal principal, IReadOnlySet<string> adminUsers)
    {
        string? studentId = AuthClaims.ReadStudentId(principal);
        if (studentId is null)
        {
            return null;
        }

        var roles = AuthClaims.ReadRoles(principal, adminUsers);

        return new ClaimUserData(
            studentId,
            principal.FindFirstValue("given_name") ?? string.Empty,
            principal.FindFirstValue("family_name") ?? string.Empty,
            principal.FindFirstValue("email"),
            AuthClaims.PrimaryRole(roles),
            principal.FindFirstValue(AuthClaims.ClassClaim));
    }
}
