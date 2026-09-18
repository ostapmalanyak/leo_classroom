using LeoClassroom.Services.Auth;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Download;

public sealed record DownloadJobView(
    long Id,
    long AssignmentId,
    DownloadJobStatus Status,
    DownloadSnapshotMode Mode,
    string? Notes,
    string? Error,
    Instant CreatedAt,
    Instant? CompletedAt,
    Instant? ExpiresAt);

public sealed record DownloadArtifact(string Path, string FileName);

public interface IDownloadService
{
    public ValueTask<OneOf<Success<long>, NotFound, Forbidden>> TriggerAsync(
        long assignmentId, DownloadSnapshotMode? mode);
    public ValueTask<OneOf<DownloadJobView, NotFound, Forbidden>> GetStatusAsync(long assignmentId, long jobId);
    public ValueTask<OneOf<DownloadArtifact, NotFound, Forbidden>> GetArtifactAsync(long assignmentId, long jobId);
}

internal sealed class DownloadService(IUnitOfWork uow, ICurrentUser currentUser, IClock clock) : IDownloadService
{
    public async ValueTask<OneOf<Success<long>, NotFound, Forbidden>> TriggerAsync(
        long assignmentId, DownloadSnapshotMode? mode)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetWithTeachersAsync(assignmentId);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!IsTeacherOf(assignment))
        {
            return new Forbidden();
        }

        var job = new DownloadJob
        {
            AssignmentId = assignmentId,
            RequestedByStudentId = currentUser.StudentId,
            Mode = mode ?? assignment.DownloadSnapshotMode,
            Status = DownloadJobStatus.Pending,
            CreatedAt = clock.GetCurrentInstant()
        };
        uow.DownloadJobRepository.Add(job);
        await uow.SaveChangesAsync();

        return new Success<long>(job.Id);
    }

    public async ValueTask<OneOf<DownloadJobView, NotFound, Forbidden>> GetStatusAsync(long assignmentId, long jobId)
    {
        OneOf<DownloadJob, NotFound, Forbidden> gated = await LoadGatedJobAsync(assignmentId, jobId);

        return gated.Match<OneOf<DownloadJobView, NotFound, Forbidden>>(
            job => new DownloadJobView(job.Id, job.AssignmentId, job.Status, job.Mode, job.Notes, job.Error,
                                       job.CreatedAt, job.CompletedAt, job.ExpiresAt),
            notFound => notFound,
            forbidden => forbidden);
    }

    public async ValueTask<OneOf<DownloadArtifact, NotFound, Forbidden>> GetArtifactAsync(long assignmentId, long jobId)
    {
        OneOf<DownloadJob, NotFound, Forbidden> gated = await LoadGatedJobAsync(assignmentId, jobId);

        return gated.Match<OneOf<DownloadArtifact, NotFound, Forbidden>>(
            job =>
            {
                if (job.Status != DownloadJobStatus.Ready || job.ArtifactPath is null || !File.Exists(job.ArtifactPath))
                {
                    return new NotFound();
                }

                return new DownloadArtifact(job.ArtifactPath, Path.GetFileName(job.ArtifactPath));
            },
            notFound => notFound,
            forbidden => forbidden);
    }

    private async ValueTask<OneOf<DownloadJob, NotFound, Forbidden>> LoadGatedJobAsync(long assignmentId, long jobId)
    {
        DownloadJob? job = await uow.DownloadJobRepository.GetByIdAsync(jobId);
        if (job is null || job.AssignmentId != assignmentId)
        {
            return new NotFound();
        }

        Assignment? assignment = await uow.AssignmentRepository.GetWithTeachersAsync(assignmentId);
        if (assignment is null)
        {
            return new NotFound();
        }
        if (!IsTeacherOf(assignment))
        {
            return new Forbidden();
        }

        return job;
    }

    private bool IsTeacherOf(Assignment assignment) =>
        currentUser.Roles.Contains(Role.Admin)
        || string.Equals(assignment.Owner.StudentId, currentUser.StudentId, StringComparison.Ordinal)
        || assignment.CoTeachers.Any(t => string.Equals(t.StudentId, currentUser.StudentId, StringComparison.Ordinal));
}
