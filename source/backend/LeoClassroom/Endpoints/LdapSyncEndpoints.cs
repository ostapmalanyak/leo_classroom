using LeoClassroom.Auth;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Ldap;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The administrative endpoints of the LDAP directory sync
/// </summary>
public static class LdapSyncEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapLdapSyncEndpoints()
        {
            var ldapSync = app.MapGroup("/api/admin/ldap-sync")
                              .WithTags("Admin")
                              .RequireAuthorization(AuthPolicies.RequireAdmin);

            ldapSync.MapPost("/", TriggerLdapSyncAsync);
        }
    }

    /// <remarks>
    ///     One transaction over the whole reconcile, exactly as <c>LdapSyncJob</c> does it - the sync must not
    ///     behave differently for being started by hand.
    /// </remarks>
    private static async ValueTask<Ok<SyncOutcome>> TriggerLdapSyncAsync(
        [FromServices] ILdapSyncService sync, [FromServices] ITransactionProvider transaction) =>
        TypedResults.Ok(await transaction.ExecuteAsync(async () => await sync.RunAsync()));
}
