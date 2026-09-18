using LeoClassroom.Services.Download;
using Quartz;

namespace LeoClassroom.Services.Scheduling;

/// <remarks>
///     **Deliberately without an explicit transaction.** The work is deleting artifact files and then
///     clearing the paths that pointed at them, and a transaction cannot cover a file deletion: the files are
///     gone before any commit, so a rollback would leave rows claiming artifacts that no longer exist - worse
///     than what it protects against. The single save is atomic on its own, and a run that fails is corrected
///     by the next one, which finds the files already missing and clears the paths then.
/// </remarks>
[DisallowConcurrentExecution]
internal sealed class DownloadCleanupJob(IDownloadCleanupService service) : IJob
{
    public static readonly JobKey Key = new("download-cleanup");

    public async Task Execute(IJobExecutionContext context) => await service.CleanupAsync();
}
