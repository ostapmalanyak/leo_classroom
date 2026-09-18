using FluentValidation;
using LeoClassroom.Auth;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using OneOf;
using ServiceNotFound = OneOf.Types.NotFound;
using ServiceSuccess = OneOf.Types.Success<LeoClassroom.Persistence.Model.Roster>;
using WriteResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Ok<LeoClassroom.Endpoints.RosterDto>,
    Microsoft.AspNetCore.Http.HttpResults.ValidationProblem,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;
using DeleteResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.NoContent,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.Conflict<LeoClassroom.Endpoints.RosterInUseDto>,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints of the custom roster resource
/// </summary>
public static class RosterEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapRosterEndpoints()
        {
            var rosters = app.MapGroup("/api/rosters")
                             .WithTags("Rosters")
                             .RequireAuthorization(AuthPolicies.RequireTeacher);

            rosters.MapGet("/", GetRostersAsync)
                   .WithName(nameof(GetRostersAsync));
            rosters.MapGet("/{id:long:min(1)}/members", GetRosterMembersAsync);
            rosters.MapPost("/", CreateRosterAsync);
            rosters.MapPut("/{id:long:min(1)}", RenameRosterAsync);
            rosters.MapDelete("/{id:long:min(1)}", DeleteRosterAsync);
            rosters.MapPost("/{id:long:min(1)}/members/{userId:long:min(1)}", AddRosterMemberAsync);
            rosters.MapDelete("/{id:long:min(1)}/members/{userId:long:min(1)}", RemoveRosterMemberAsync);
        }
    }

    private static async ValueTask<Ok<IReadOnlyCollection<RosterOverview>>> GetRostersAsync(
        [FromServices] ICustomRosterService service) =>
        TypedResults.Ok(await service.ListAsync());

    private static async ValueTask<Results<Ok<IReadOnlyCollection<UserSummary>>, NotFound>> GetRosterMembersAsync(
        [FromRoute] long id, [FromServices] ICustomRosterService service)
    {
        var result = await service.ListMembersAsync(id);

        return result.ToOk();
    }

    private static async ValueTask<Results<CreatedAtRoute<RosterDto>, ValidationProblem, NotFound>>
        CreateRosterAsync([FromBody] RosterWriteRequest request, [FromServices] ICustomRosterService service,
                          [FromServices] ITransactionProvider transaction)
    {
        if (new RosterWriteRequest.Validator().Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        await transaction.BeginTransactionAsync();
        var result = await service.CreateAsync(request.Name);

        return await result.Match(OnCreatedAsync, OnNotFoundAsync);

        async ValueTask<Results<CreatedAtRoute<RosterDto>, ValidationProblem, NotFound>> OnCreatedAsync(
            ServiceSuccess success)
        {
            await transaction.CommitAsync();

            return TypedResults.CreatedAtRoute(RosterDto.FromRoster(success.Value), nameof(GetRostersAsync));
        }

        // left uncommitted on purpose - scoped unit-of-work disposal rolls the transaction back
        static ValueTask<Results<CreatedAtRoute<RosterDto>, ValidationProblem, NotFound>> OnNotFoundAsync(
            ServiceNotFound notFound) =>
            ValueTask.FromResult<Results<CreatedAtRoute<RosterDto>, ValidationProblem, NotFound>>(
                TypedResults.NotFound());
    }

    private static async ValueTask<WriteResult> RenameRosterAsync(
        [FromRoute] long id, [FromBody] RosterWriteRequest request, [FromServices] ICustomRosterService service,
        [FromServices] ITransactionProvider transaction)
    {
        if (new RosterWriteRequest.Validator().Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        await transaction.BeginTransactionAsync();
        var result = await service.RenameAsync(id, request.Name);

        return await result.Match(OnRenamedAsync, OnNotFoundAsync, OnForbiddenAsync);

        async ValueTask<WriteResult> OnRenamedAsync(ServiceSuccess success)
        {
            await transaction.CommitAsync();

            return TypedResults.Ok(RosterDto.FromRoster(success.Value));
        }

        static ValueTask<WriteResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<WriteResult>(TypedResults.NotFound());

        static ValueTask<WriteResult> OnForbiddenAsync(Forbidden forbidden) =>
            ValueTask.FromResult<WriteResult>(ApiResults.Forbidden());
    }

    private static async ValueTask<DeleteResult> DeleteRosterAsync(
        [FromRoute] long id, [FromServices] ICustomRosterService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.DeleteAsync(id);

        return await result.Match(OnDeletedAsync, OnNotFoundAsync, OnForbiddenAsync, OnInUseAsync);

        async ValueTask<DeleteResult> OnDeletedAsync(OneOf.Types.Success success)
        {
            await transaction.CommitAsync();

            return TypedResults.NoContent();
        }

        static ValueTask<DeleteResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<DeleteResult>(TypedResults.NotFound());

        static ValueTask<DeleteResult> OnForbiddenAsync(Forbidden forbidden) =>
            ValueTask.FromResult<DeleteResult>(ApiResults.Forbidden());

        static ValueTask<DeleteResult> OnInUseAsync(ICustomRosterService.InUse inUse) =>
            ValueTask.FromResult<DeleteResult>(TypedResults.Conflict(new RosterInUseDto(inUse.CourseCount)));
    }

    private static async ValueTask<Results<NoContent, NotFound, ProblemHttpResult>> AddRosterMemberAsync(
        [FromRoute] long id, [FromRoute] long userId, [FromServices] ICustomRosterService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        OneOf<OneOf.Types.Success, ServiceNotFound, Forbidden> result = await service.AddMemberAsync(id, userId);

        return await result.CommitNoContentAsync(transaction);
    }

    private static async ValueTask<Results<NoContent, NotFound, ProblemHttpResult>> RemoveRosterMemberAsync(
        [FromRoute] long id, [FromRoute] long userId, [FromServices] ICustomRosterService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        OneOf<OneOf.Types.Success, ServiceNotFound, Forbidden> result = await service.RemoveMemberAsync(id, userId);

        return await result.CommitNoContentAsync(transaction);
    }
}

public sealed record RosterWriteRequest(string Name)
{
    public sealed class Validator : AbstractValidator<RosterWriteRequest>
    {
        private const int MaxNameLength = 200;

        public Validator() => RuleFor(r => r.Name).NotEmpty().MaximumLength(MaxNameLength);
    }
}

public sealed record RosterInUseDto(int CourseCount);

public sealed record RosterDto(long Id, string Name, RosterKind Kind, long? OwnerId)
{
    public static RosterDto FromRoster(Roster roster) => new(roster.Id, roster.Name, roster.Kind, roster.OwnerId);
}
