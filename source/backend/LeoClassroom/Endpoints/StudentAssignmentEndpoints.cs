using LeoClassroom.Auth;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services;
using LeoClassroom.Services.Provisioning;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ServiceNotFound = OneOf.Types.NotFound;
using ServiceSuccess = OneOf.Types.Success<LeoClassroom.Persistence.Model.Acceptance>;
using AcceptResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Accepted<LeoClassroom.Endpoints.SubmissionDto>,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.Conflict,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;
using RetryResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Accepted<LeoClassroom.Endpoints.SubmissionDto>,
    Microsoft.AspNetCore.Http.HttpResults.NotFound>;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints a student uses to see and accept their own assignments
/// </summary>
public static class StudentAssignmentEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapStudentAssignmentEndpoints()
        {
            var mine = app.MapGroup("/api/assignments")
                          .WithTags("Student assignments")
                          .RequireAuthorization(AuthPolicies.RequireStudent);

            mine.MapGet("/mine", GetMyAssignmentsAsync);
            mine.MapGet("/{id:long:min(1)}/info", GetAssignmentInfoAsync);
            mine.MapGet("/{id:long:min(1)}/submission", GetSubmissionAsync);
            mine.MapPost("/{id:long:min(1)}/accept", AcceptAssignmentAsync);
            mine.MapPost("/{id:long:min(1)}/feedback/read", ConfirmFeedbackReadAsync);
            mine.MapPost("/{id:long:min(1)}/retry", RetryProvisioningAsync);
        }
    }

    private static async ValueTask<Ok<StudentAssignmentList>> GetMyAssignmentsAsync(
        [FromServices] IStudentAssignmentService service) =>
        TypedResults.Ok(await service.ListAsync());

    private static async ValueTask<Results<Ok<AssignmentInfoDto>, NotFound, ProblemHttpResult>>
        GetAssignmentInfoAsync([FromRoute] long id, [FromServices] IStudentAssignmentService service)
    {
        var result = await service.GetForStudentAsync(id);

        return result.ToOk(AssignmentInfoDto.FromView);
    }

    private static async ValueTask<Results<Ok<SubmissionDto>, NotFound, ProblemHttpResult>> GetSubmissionAsync(
        [FromRoute] long id, [FromServices] IStudentAssignmentService service)
    {
        var result = await service.GetForStudentAsync(id);

        return result.ToOk(SubmissionDto.FromView);
    }

    private static async ValueTask<AcceptResult> AcceptAssignmentAsync(
        [FromRoute] long id, [FromServices] IStudentAssignmentService service,
        [FromServices] ITransactionProvider transaction, [FromServices] IProvisioningQueue queue)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.AcceptAsync(id);

        return await result.Match(OnAcceptedAsync, OnNotFoundAsync, OnForbiddenAsync, OnConflictAsync);

        async ValueTask<AcceptResult> OnAcceptedAsync(ServiceSuccess success)
        {
            await transaction.CommitAsync();
            await queue.EnqueueAsync(success.Value.Id);

            return TypedResults.Accepted((string?) null, SubmissionDto.FromAcceptance(success.Value));
        }

        // left uncommitted on purpose - scoped unit-of-work disposal rolls the transaction back
        static ValueTask<AcceptResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<AcceptResult>(TypedResults.NotFound());

        static ValueTask<AcceptResult> OnForbiddenAsync(Forbidden forbidden) =>
            ValueTask.FromResult<AcceptResult>(ApiResults.Forbidden());

        static ValueTask<AcceptResult> OnConflictAsync(Services.Forgejo.AlreadyExists alreadyExists) =>
            ValueTask.FromResult<AcceptResult>(TypedResults.Conflict());
    }

    private static async ValueTask<Results<NoContent, NotFound>> ConfirmFeedbackReadAsync(
        [FromRoute] long id, [FromServices] IStudentAssignmentService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.ConfirmFeedbackReadAsync(id);

        return await result.Match(OnConfirmedAsync, OnNotFoundAsync);

        async ValueTask<Results<NoContent, NotFound>> OnConfirmedAsync(OneOf.Types.Success success)
        {
            await transaction.CommitAsync();

            return TypedResults.NoContent();
        }

        static ValueTask<Results<NoContent, NotFound>> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<Results<NoContent, NotFound>>(TypedResults.NotFound());
    }

    private static async ValueTask<RetryResult> RetryProvisioningAsync(
        [FromRoute] long id, [FromServices] IStudentAssignmentService service,
        [FromServices] ITransactionProvider transaction, [FromServices] IProvisioningQueue queue)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.RetryAsync(id);

        return await result.Match(OnRetriedAsync, OnNotFoundAsync);

        async ValueTask<RetryResult> OnRetriedAsync(AcceptanceRetry retry)
        {
            await transaction.CommitAsync();
            if (retry.NeedsProvisioning)
            {
                await queue.EnqueueAsync(retry.Acceptance.Id);
            }

            return TypedResults.Accepted((string?) null, SubmissionDto.FromAcceptance(retry.Acceptance));
        }

        static ValueTask<RetryResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<RetryResult>(TypedResults.NotFound());
    }
}

public sealed record AssignmentInfoDto(
    long Id, string Title, string? HintsInstructions, Instant? Deadline, DeadlineKind DeadlineKind, bool Accepted)
{
    public static AssignmentInfoDto FromView(StudentAssignmentView view) => new(
        view.Assignment.Id, view.Assignment.Title, view.Assignment.HintsInstructions, view.Assignment.Deadline,
        view.Assignment.DeadlineKind, view.Acceptance is not null);
}

public sealed record SubmissionDto(
    long AssignmentId, string Title, string? Description, Instant? Deadline, DeadlineKind DeadlineKind,
    SubmissionStatus? Status, string? RepoUrl, bool Late, Instant? LateSince,
    FeedbackState FeedbackState, Instant? FeedbackReadAt)
{
    public static SubmissionDto FromView(StudentAssignmentView view) => new(
        view.Assignment.Id, view.Assignment.Title, view.Assignment.Description, view.Assignment.Deadline,
        view.Assignment.DeadlineKind, view.Acceptance?.Status,
        view.Acceptance?.Status == SubmissionStatus.Ready ? view.Acceptance.RepoUrl : null,
        view.Acceptance?.Late ?? false, view.Acceptance?.LateSince,
        view.Acceptance?.FeedbackState ?? FeedbackState.None, view.Acceptance?.FeedbackReadAt);

    public static SubmissionDto FromAcceptance(Acceptance acceptance) => new(
        acceptance.AssignmentId, string.Empty, null, null, DeadlineKind.None, acceptance.Status,
        acceptance.Status == SubmissionStatus.Ready ? acceptance.RepoUrl : null,
        acceptance.Late, acceptance.LateSince, acceptance.FeedbackState, acceptance.FeedbackReadAt);
}
