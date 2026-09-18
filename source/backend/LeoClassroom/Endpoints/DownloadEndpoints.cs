using LeoClassroom.Auth;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Download;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ServiceNotFound = OneOf.Types.NotFound;
using TriggerResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.AcceptedAtRoute<LeoClassroom.Endpoints.DownloadJobAcceptedDto>,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints of the "download all submissions" job of an assignment
/// </summary>
public static class DownloadEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapDownloadEndpoints()
        {
            var downloads = app.MapGroup("/api/assignments/{assignmentId:long:min(1)}/download")
                               .WithTags("Downloads")
                               .RequireAuthorization(AuthPolicies.RequireTeacher);

            downloads.MapPost("/", TriggerDownloadAsync);
            downloads.MapGet("/{jobId:long:min(1)}", GetDownloadStatusAsync)
                     .WithName(nameof(GetDownloadStatusAsync));
            downloads.MapGet("/{jobId:long:min(1)}/artifact", GetDownloadArtifactAsync);
        }
    }

    private static async ValueTask<TriggerResult> TriggerDownloadAsync(
        [FromRoute] long assignmentId, [FromBody] DownloadTriggerRequest? request,
        [FromServices] IDownloadService service, [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.TriggerAsync(assignmentId, request?.Mode);

        return await result.Match(OnAcceptedAsync, OnNotFoundAsync, OnForbiddenAsync);

        async ValueTask<TriggerResult> OnAcceptedAsync(OneOf.Types.Success<long> success)
        {
            await transaction.CommitAsync();

            return TypedResults.AcceptedAtRoute(new DownloadJobAcceptedDto(success.Value),
                                                nameof(GetDownloadStatusAsync),
                                                new { assignmentId, jobId = success.Value });
        }

        // left uncommitted on purpose - scoped unit-of-work disposal rolls the transaction back
        static ValueTask<TriggerResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<TriggerResult>(TypedResults.NotFound());

        static ValueTask<TriggerResult> OnForbiddenAsync(Forbidden forbidden) =>
            ValueTask.FromResult<TriggerResult>(ApiResults.Forbidden());
    }

    private static async ValueTask<Results<Ok<DownloadJobView>, NotFound, ProblemHttpResult>>
        GetDownloadStatusAsync([FromRoute] long assignmentId, [FromRoute] long jobId,
                               [FromServices] IDownloadService service)
    {
        var result = await service.GetStatusAsync(assignmentId, jobId);

        return result.ToOk();
    }

    private static async ValueTask<Results<PhysicalFileHttpResult, NotFound, ProblemHttpResult>>
        GetDownloadArtifactAsync([FromRoute] long assignmentId, [FromRoute] long jobId,
                                 [FromServices] IDownloadService service)
    {
        var result = await service.GetArtifactAsync(assignmentId, jobId);

        return result.Match<Results<PhysicalFileHttpResult, NotFound, ProblemHttpResult>>(
            artifact => TypedResults.PhysicalFile(artifact.Path, "application/zip", artifact.FileName),
            notFound => TypedResults.NotFound(),
            forbidden => ApiResults.Forbidden());
    }
}

public sealed record DownloadTriggerRequest(DownloadSnapshotMode? Mode);

public sealed record DownloadJobAcceptedDto(long Id);
