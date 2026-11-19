using FluentValidation;
using LeoClassroom.Auth;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceNotFound = OneOf.Types.NotFound;
using ServiceSuccess = OneOf.Types.Success<LeoClassroom.Persistence.Model.Assignment>;
using CreateResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.CreatedAtRoute<LeoClassroom.Endpoints.AssignmentDto>,
    Microsoft.AspNetCore.Http.HttpResults.ValidationProblem,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;
using FeedbackPrResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Ok<LeoClassroom.Endpoints.FeedbackPrDto>,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints a teacher uses to author and review assignments
/// </summary>
public static class AssignmentEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapAssignmentEndpoints()
        {
            var assignments = app.MapGroup("/api/assignments")
                                 .WithTags("Assignments")
                                 .RequireAuthorization(AuthPolicies.RequireTeacher);

            assignments.MapPost("/", CreateAssignmentAsync);
            assignments.MapPost("/archive-preview", PreviewStarterArchiveAsync)
                       .DisableAntiforgery()
                       .WithMetadata(new RequestSizeLimitAttribute(50 * 1024 * 1024 + 128 * 1024));
            assignments.MapGet("/course/{courseId:long:min(1)}", GetForCourseAsync);
            assignments.MapGet("/{id:long:min(1)}/edit", GetAssignmentForEditAsync)
                       .WithName(nameof(GetAssignmentForEditAsync));
            assignments.MapPut("/{id:long:min(1)}", UpdateAssignmentAsync);
            assignments.MapDelete("/{id:long:min(1)}", DeleteAssignmentAsync);
            assignments.MapPost("/{id:long:min(1)}/publish", PublishAssignmentAsync);
            assignments.MapPost("/{id:long:min(1)}/teachers", AddCoTeacherAsync);
            assignments.MapDelete("/{id:long:min(1)}/teachers/{userId:long:min(1)}", RemoveCoTeacherAsync);
            assignments.MapGet("/{id:long:min(1)}/students", GetStudentListAsync);
            assignments.MapGet("/{id:long:min(1)}/accept-link", GetAcceptLinkAsync);
            assignments.MapGet("/{id:long:min(1)}/commit-summary", GetCommitSummaryAsync);
            assignments.MapGet("/acceptances/{acceptanceId:long:min(1)}/analytics", GetAnalyticsAsync);
            assignments.MapPost("/acceptances/{acceptanceId:long:min(1)}/feedback-pr", OpenFeedbackPrAsync);
        }
    }

    private static async ValueTask<Results<Ok<IReadOnlyCollection<AssignmentDto>>, NotFound, ForbidHttpResult>>
        GetForCourseAsync([FromRoute] long courseId, [FromServices] IAssignmentService service)
    {
        var result = await service.GetForCourseAsync(courseId);

        return result.Match<Results<Ok<IReadOnlyCollection<AssignmentDto>>, NotFound, ForbidHttpResult>>(
            assignments => TypedResults.Ok<IReadOnlyCollection<AssignmentDto>>(
                assignments.Select(AssignmentDto.FromAssignment).ToArray()),
            _ => TypedResults.NotFound(),
            _ => TypedResults.Forbid());
    }

    private static async ValueTask<CreateResult> CreateAssignmentAsync(
        [FromBody] AssignmentCreateRequest request, [FromServices] IAssignmentService service,
        [FromServices] ITransactionProvider transaction,
        [FromServices] IClock clock,
        [FromServices] IStarterArchiveStorage archiveStorage,
        [FromServices] IRepoProvisioningService repoProvisioning,
        [FromServices] IForgejoClient forgejo,
        [FromServices] ILogger<Program> logger)
    {
        if (new AssignmentCreateRequest.Validator(clock).Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        if (request.StarterArchiveUploadId is not { } uploadId)
        {
            return await CreateWithoutArchiveAsync(request, service, transaction);
        }

        var courseAccess = await service.GetForCourseAsync(request.CourseId);
        CreateResult? accessFailure = courseAccess.Match<CreateResult?>(
            assignments => null,
            notFound => TypedResults.NotFound(),
            forbidden => ApiResults.Forbidden());
        if (accessFailure is not null)
        {
            return accessFailure;
        }

        StarterArchiveFile? retained = archiveStorage.RetainPending(uploadId);
        if (retained is null)
        {
            return TypedResults.ValidationProblem(
                Validation.Failure(nameof(request.StarterArchiveUploadId),
                                   "The confirmed ZIP upload has expired. Upload it again."));
        }

        if (request.StarterSourceKind == StarterSourceKind.Archive)
        {
            return await CreateDownloadOnlyArchiveAsync(request, retained, service, transaction, archiveStorage,
                                                         logger);
        }

        var templateResult = await repoProvisioning.CreateArchiveStarterAsync(
            request.CourseId, request.Title, retained.Path);

        return await templateResult.Match<ValueTask<CreateResult>>(
            template => CreateWithArchiveRepositoryAsync(request, template, retained, service, transaction,
                                                         archiveStorage, forgejo, logger),
            notFound =>
            {
                TryDeleteRetainedArchive(archiveStorage, retained, logger);

                return ValueTask.FromResult<CreateResult>(TypedResults.NotFound());
            },
            error =>
            {
                TryDeleteRetainedArchive(archiveStorage, retained, logger);

                return ValueTask.FromResult<CreateResult>(ApiResults.BadGateway(error.Reason));
            });
    }

    private static async ValueTask<CreateResult> CreateWithoutArchiveAsync(
        AssignmentCreateRequest request, IAssignmentService service, ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.CreateAsync(request.CourseId, request.ToSettings());

        return await result.Match(OnCreatedAsync, OnNotFoundAsync, OnForbiddenAsync, OnUpstreamFailureAsync);

        async ValueTask<CreateResult> OnCreatedAsync(ServiceSuccess success)
        {
            // committing is explicit in the success case, rolling back is the default
            await transaction.CommitAsync();

            return TypedResults.CreatedAtRoute(AssignmentDto.FromAssignment(success.Value),
                                               nameof(GetAssignmentForEditAsync), new { id = success.Value.Id });
        }

        // left uncommitted on purpose - scoped unit-of-work disposal rolls the transaction back
        static ValueTask<CreateResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<CreateResult>(TypedResults.NotFound());

        static ValueTask<CreateResult> OnForbiddenAsync(Forbidden forbidden) =>
            ValueTask.FromResult<CreateResult>(ApiResults.Forbidden());

        static ValueTask<CreateResult> OnUpstreamFailureAsync(Services.Forgejo.ForgejoError error) =>
            ValueTask.FromResult<CreateResult>(ApiResults.BadGateway(error.Reason));
    }

    private static async ValueTask<CreateResult> CreateDownloadOnlyArchiveAsync(
        AssignmentCreateRequest request, StarterArchiveFile archive, IAssignmentService service,
        ITransactionProvider transaction, IStarterArchiveStorage archiveStorage, ILogger<Program> logger)
    {
        await transaction.BeginTransactionAsync();
        AssignmentSettings settings = request.ToSettings() with { ArchivePath = archive.Path };
        var result = await service.CreateAsync(request.CourseId, settings);

        return await result.Match(OnCreatedAsync, OnNotFoundAsync, OnForbiddenAsync, OnUpstreamFailureAsync);

        async ValueTask<CreateResult> OnCreatedAsync(ServiceSuccess success)
        {
            await transaction.CommitAsync();

            return TypedResults.CreatedAtRoute(AssignmentDto.FromAssignment(success.Value),
                                               nameof(GetAssignmentForEditAsync), new { id = success.Value.Id });
        }

        async ValueTask<CreateResult> OnNotFoundAsync(ServiceNotFound notFound)
        {
            await transaction.RollbackAsync();
            TryDeleteRetainedArchive(archiveStorage, archive, logger);

            return TypedResults.NotFound();
        }

        async ValueTask<CreateResult> OnForbiddenAsync(Forbidden forbidden)
        {
            await transaction.RollbackAsync();
            TryDeleteRetainedArchive(archiveStorage, archive, logger);

            return ApiResults.Forbidden();
        }

        async ValueTask<CreateResult> OnUpstreamFailureAsync(Services.Forgejo.ForgejoError error)
        {
            await transaction.RollbackAsync();
            TryDeleteRetainedArchive(archiveStorage, archive, logger);

            return ApiResults.BadGateway(error.Reason);
        }
    }

    private static async ValueTask<CreateResult> CreateWithArchiveRepositoryAsync(
        AssignmentCreateRequest request, ForgejoRepo template, StarterArchiveFile archive,
        IAssignmentService service, ITransactionProvider transaction, IStarterArchiveStorage archiveStorage,
        IForgejoClient forgejo, ILogger<Program> logger)
    {
        await transaction.BeginTransactionAsync();
        AssignmentSettings settings = request.ToSettings() with
        {
            StarterRepoUrl = template.CloneUrl,
            StarterTemplateRepoUrl = template.CloneUrl
        };
        var result = await service.CreateAsync(request.CourseId, settings);

        return await result.Match(OnCreatedAsync, OnNotFoundAsync, OnForbiddenAsync, OnUpstreamFailureAsync);

        async ValueTask<CreateResult> OnCreatedAsync(ServiceSuccess success)
        {
            await transaction.CommitAsync();
            TryDeleteRetainedArchive(archiveStorage, archive, logger);

            return TypedResults.CreatedAtRoute(AssignmentDto.FromAssignment(success.Value),
                                               nameof(GetAssignmentForEditAsync), new { id = success.Value.Id });
        }

        async ValueTask<CreateResult> OnNotFoundAsync(ServiceNotFound notFound)
        {
            await CleanupFailedArchiveAssignmentAsync();

            return TypedResults.NotFound();
        }

        async ValueTask<CreateResult> OnForbiddenAsync(Forbidden forbidden)
        {
            await CleanupFailedArchiveAssignmentAsync();

            return ApiResults.Forbidden();
        }

        async ValueTask<CreateResult> OnUpstreamFailureAsync(Services.Forgejo.ForgejoError error)
        {
            await CleanupFailedArchiveAssignmentAsync();

            return ApiResults.BadGateway(error.Reason);
        }

        async ValueTask CleanupFailedArchiveAssignmentAsync()
        {
            await transaction.RollbackAsync();
            TryDeleteRetainedArchive(archiveStorage, archive, logger);
            string[] repoParts = template.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (repoParts.Length != 2)
            {
                logger.LogError("Could not clean up archive starter repository with unexpected name {Repo}",
                                template.FullName);

                return;
            }

            var deleted = await forgejo.DeleteRepoAsync(repoParts[0], repoParts[1]);
            deleted.Switch(
                success => { },
                error => logger.LogWarning("Could not clean up archive starter repository {Repo}: {Reason}",
                                           template.FullName, error.Reason));
        }
    }

    private static void TryDeleteRetainedArchive(
        IStarterArchiveStorage archiveStorage, StarterArchiveFile archive, ILogger<Program> logger)
    {
        try
        {
            archiveStorage.Delete(archive.Path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not remove retained starter archive after assignment creation failed");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Could not remove retained starter archive after assignment creation failed");
        }
    }

    private static async ValueTask<Results<Ok<StarterArchivePreviewDto>, ValidationProblem>>
        PreviewStarterArchiveAsync([FromForm] IFormFile archive,
                                   [FromServices] IStarterArchiveStorage storage,
                                   CancellationToken cancellationToken)
    {
        await using Stream source = archive.OpenReadStream();
        var result = await storage.StoreAsync(source, archive.FileName, cancellationToken);

        return result.Match<Results<Ok<StarterArchivePreviewDto>, ValidationProblem>>(
            file => TypedResults.Ok(new StarterArchivePreviewDto(file.Id, file.FileName, file.Entries)),
            error => TypedResults.ValidationProblem(Validation.Failure("archive", error.Message)));
    }

    private static async ValueTask<Results<Ok<AssignmentDto>, NotFound, ProblemHttpResult>>
        GetAssignmentForEditAsync([FromRoute] long id, [FromServices] IAssignmentService service)
    {
        var result = await service.GetForEditAsync(id);

        return result.ToOk(AssignmentDto.FromAssignment);
    }

    private static async ValueTask<Results<Ok<AssignmentDto>, ValidationProblem, NotFound, ProblemHttpResult>>
        UpdateAssignmentAsync([FromRoute] long id, [FromBody] AssignmentUpdateRequest request,
                              [FromServices] IAssignmentService service,
                              [FromServices] ITransactionProvider transaction, [FromServices] IClock clock)
    {
        if (new AssignmentUpdateRequest.Validator(clock).Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        await transaction.BeginTransactionAsync();
        var result = await service.EditAsync(id, request.ToSettings());

        return await result.CommitOkOrInvalidAsync(transaction,
                                                   success => AssignmentDto.FromAssignment(success.Value));
    }

    private static async ValueTask<Results<NoContent, NotFound, ProblemHttpResult>> PublishAssignmentAsync(
        [FromRoute] long id, [FromServices] IAssignmentService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.PublishAsync(id);

        return await result.CommitNoContentAsync(transaction);
    }

    private static async ValueTask<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAssignmentAsync(
        [FromRoute] long id, [FromServices] IAssignmentService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.DeleteAsync(id);

        return await result.CommitNoContentAsync(transaction);
    }

    private static async ValueTask<Results<NoContent, ValidationProblem, NotFound, ProblemHttpResult>>
        AddCoTeacherAsync([FromRoute] long id, [FromBody] AssignmentTeacherRequest request,
                          [FromServices] IAssignmentService service,
                          [FromServices] ITransactionProvider transaction)
    {
        if (new AssignmentTeacherRequest.Validator().Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        await transaction.BeginTransactionAsync();
        var result = await service.AddCoTeacherAsync(id, request.UserId);

        return await result.CommitNoContentOrInvalidAsync(transaction);
    }

    private static async ValueTask<Results<NoContent, NotFound, ProblemHttpResult>> RemoveCoTeacherAsync(
        [FromRoute] long id, [FromRoute] long userId, [FromServices] IAssignmentService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.RemoveCoTeacherAsync(id, userId);

        return await result.CommitNoContentAsync(transaction);
    }

    private static async ValueTask<Results<Ok<IReadOnlyCollection<StudentSubmission>>, NotFound, ProblemHttpResult>>
        GetStudentListAsync([FromRoute] long id, [FromServices] IAssignmentService service)
    {
        var result = await service.GetStudentListAsync(id);

        return result.ToOk();
    }

    private static async ValueTask<Results<Ok<AcceptLinkDto>, NotFound, ProblemHttpResult>> GetAcceptLinkAsync(
        [FromRoute] long id, [FromServices] IAssignmentService service, [FromServices] IOptions<Settings> settings)
    {
        var result = await service.GetForEditAsync(id);

        return result.ToOk(assignment => new AcceptLinkDto(
                               assignment.Id,
                               $"{settings.Value.ClientOrigin.TrimEnd('/')}/my-assignments/{assignment.Id}"));
    }

    private static async ValueTask<Results<Ok<IReadOnlyCollection<CommitSummaryView>>, NotFound, ProblemHttpResult>>
        GetCommitSummaryAsync([FromRoute] long id, [FromServices] ISubmissionReviewService review)
    {
        var result = await review.GetCommitSummaryAsync(id);

        return result.ToOk();
    }

    private static async ValueTask<Results<Ok<CommitAnalyticsView>, NotFound, ProblemHttpResult>> GetAnalyticsAsync(
        [FromRoute] long acceptanceId, [FromServices] ISubmissionReviewService review)
    {
        var result = await review.GetAnalyticsAsync(acceptanceId);

        return result.Match<Results<Ok<CommitAnalyticsView>, NotFound, ProblemHttpResult>>(
            analytics => TypedResults.Ok(analytics),
            _ => TypedResults.NotFound(),
            _ => ApiResults.Forbidden(),
            error => ApiResults.BadGateway(error.Reason));
    }

    private static async ValueTask<FeedbackPrResult> OpenFeedbackPrAsync(
        [FromRoute] long acceptanceId, [FromServices] ISubmissionReviewService review,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await review.OpenFeedbackPrAsync(acceptanceId);

        return await result.Match(OnOpenedAsync, OnNotFoundAsync, OnForbiddenAsync, OnUpstreamFailureAsync);

        async ValueTask<FeedbackPrResult> OnOpenedAsync(OneOf.Types.Success<FeedbackPr> success)
        {
            await transaction.CommitAsync();

            return TypedResults.Ok(new FeedbackPrDto(success.Value.Number, success.Value.HtmlUrl));
        }

        static ValueTask<FeedbackPrResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<FeedbackPrResult>(TypedResults.NotFound());

        static ValueTask<FeedbackPrResult> OnForbiddenAsync(Forbidden forbidden) =>
            ValueTask.FromResult<FeedbackPrResult>(ApiResults.Forbidden());

        static ValueTask<FeedbackPrResult> OnUpstreamFailureAsync(Services.Forgejo.ForgejoError error) =>
            ValueTask.FromResult<FeedbackPrResult>(ApiResults.BadGateway(error.Reason));
    }
}

public sealed record FeedbackPrDto(long Number, string HtmlUrl);

public sealed record StarterArchivePreviewDto(string UploadId, string FileName,
                                              IReadOnlyCollection<string> Entries);

public sealed record AcceptLinkDto(long AssignmentId, string AcceptLink);

public sealed record AssignmentTeacherRequest(long UserId)
{
    public sealed class Validator : AbstractValidator<AssignmentTeacherRequest>
    {
        public Validator() => RuleFor(r => r.UserId).GreaterThan(0);
    }
}

public sealed record AssignmentDto(
    long Id,
    long CourseId,
    long OwnerId,
    string Title,
    string Slug,
    string? Description,
    string? HintsInstructions,
    Instant? Deadline,
    DeadlineKind DeadlineKind,
    bool HardDeadlineRevokesRead,
    StarterSourceKind StarterSourceKind,
    string? StarterRepoUrl,
    string? ReadmeMarkdown,
    bool AutoDeleteEnabled,
    Instant? AutoDeleteOn,
    DownloadSnapshotMode DownloadSnapshotMode,
    bool IsPublished,
    Instant? PublishAt,
    bool HasStarterArchive,
    string? StarterTemplateRepoUrl)
{
    public static AssignmentDto FromAssignment(Assignment a) => new(
        a.Id, a.CourseId, a.OwnerId, a.Title, a.Slug, a.Description, a.HintsInstructions, a.Deadline,
        a.DeadlineKind, a.HardDeadlineRevokesRead, a.StarterSourceKind, a.StarterRepoUrl, a.ReadmeMarkdown,
        a.AutoDeleteEnabled, a.AutoDeleteOn, a.DownloadSnapshotMode, a.IsPublished, a.PublishAt,
        a.StarterSourceKind == StarterSourceKind.Archive && a.ArchivePath is not null,
        a.StarterTemplateRepoUrl);
}
