using FluentValidation;
using LeoClassroom.Auth;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services;
using LeoClassroom.Services.Util;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ServiceNotFound = OneOf.Types.NotFound;
using ServiceSuccess = OneOf.Types.Success<LeoClassroom.Services.UserDeletionImpact>;
using DeleteResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Ok<LeoClassroom.Services.UserDeletionImpact>,
    Microsoft.AspNetCore.Http.HttpResults.NotFound>;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The administrative endpoints for permanently removing users
/// </summary>
/// <remarks>
///     Deletion here is a cascade: a user takes their own submissions with them, plus the rosters, courses and
///     assignments they own and every other student's submissions inside those. Both destructive endpoints have a
///     preview counterpart that reports exactly that blast radius and changes nothing, and the bulk purge
///     additionally refuses to run without an explicit confirmation.
/// </remarks>
public static class AdminUserEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapAdminUserEndpoints()
        {
            var users = app.MapGroup("/api/admin/users")
                           .WithTags("Admin")
                           .RequireAuthorization(AuthPolicies.RequireAdmin);

            users.MapGet("/{id:long:min(1)}/deletion-impact", PreviewUserDeletionAsync);
            users.MapDelete("/{id:long:min(1)}", DeleteUserAsync);
            users.MapGet("/purge-preview", PreviewExpiredPurgeAsync);
            users.MapPost("/purge", PurgeExpiredAsync);
        }
    }

    private static async ValueTask<DeleteResult> PreviewUserDeletionAsync(
        [FromRoute] long id, [FromServices] IUserDeletionService deletion)
    {
        var result = await deletion.PreviewAsync(id);

        return result.ToOk();
    }

    private static async ValueTask<DeleteResult> DeleteUserAsync(
        [FromRoute] long id, [FromServices] IUserDeletionService deletion,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await deletion.DeleteAsync(id);

        return await result.Match(OnDeletedAsync, OnNotFoundAsync);

        // answers 200 with what was destroyed rather than a bare 204, so the record of the cascade reaches the
        // caller and not only the audit log
        async ValueTask<DeleteResult> OnDeletedAsync(ServiceSuccess success)
        {
            await transaction.CommitAsync();

            return TypedResults.Ok(success.Value);
        }

        static ValueTask<DeleteResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<DeleteResult>(TypedResults.NotFound());
    }

    private static async ValueTask<Ok<IReadOnlyCollection<UserDeletionImpact>>> PreviewExpiredPurgeAsync(
        [FromServices] IUserDeletionService deletion) =>
        TypedResults.Ok(await deletion.PreviewExpiredAsync());

    private static async ValueTask<Results<Ok<PurgeOutcome>, ValidationProblem>> PurgeExpiredAsync(
        [FromBody] PurgeRequest request, [FromServices] IUserDeletionService deletion)
    {
        if (new PurgeRequest.Validator().Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        // no transaction on purpose: the purge deletes one user at a time and reports what it managed, so a single
        // failure does not undo the users already removed
        return TypedResults.Ok(await deletion.PurgeExpiredAsync());
    }
}

/// <param name="Confirm">
///     Must be true. A bulk permanent deletion should not be one mistyped URL away, so the request has to say so.
/// </param>
/// <remarks>
///     There is deliberately no retention parameter: how long a departed user is kept is
///     <see cref="RetentionPolicy" />, not something a caller chooses per request.
/// </remarks>
public sealed record PurgeRequest(bool Confirm)
{
    public sealed class Validator : AbstractValidator<PurgeRequest>
    {
        public Validator() =>
            RuleFor(r => r.Confirm)
                .Equal(true)
                .WithMessage("This permanently deletes users and their courses; set confirm to true to proceed.");
    }
}
