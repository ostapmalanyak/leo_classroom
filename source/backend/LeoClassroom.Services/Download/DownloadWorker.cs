using System.IO.Compression;
using System.Text;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.Download;

public interface IDownloadJobProcessor
{
    public ValueTask<bool> ProcessNextAsync(CancellationToken cancellationToken);
}

internal sealed class DownloadJobProcessor(
    IUnitOfWork uow,
    IGitService git,
    ISubmissionSnapshotResolver resolver,
    IClock clock,
    IOptions<DownloadSettings> options,
    ILogger<DownloadJobProcessor> logger) : IDownloadJobProcessor
{
    public async ValueTask<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        DownloadJob? job = await uow.DownloadJobRepository.GetNextPendingAsync();
        if (job is null)
        {
            return false;
        }

        job.Status = DownloadJobStatus.Running;
        job.StartedAt = clock.GetCurrentInstant();
        await uow.SaveChangesAsync();

        DownloadSettings settings = options.Value;
        string workDir = Path.Combine(Path.GetTempPath(), $"leo-download-{job.Id}-{Guid.NewGuid():N}");
        try
        {
            await RunJobAsync(job, settings, workDir, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Download job {JobId} failed", job.Id);
            job.Status = DownloadJobStatus.Failed;

            // the exception text is logged, not stored: it reaches a teacher through the job status endpoint and
            // can carry server paths and connection details
            job.Error = "the export failed unexpectedly, see the server log";
        }
        finally
        {
            TryDeleteDirectory(workDir);
            job.CompletedAt = clock.GetCurrentInstant();
            await uow.SaveChangesAsync();
        }

        return true;
    }

    private async ValueTask RunJobAsync(
        DownloadJob job, DownloadSettings settings, string workDir, CancellationToken cancellationToken)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetByIdAsync(job.AssignmentId);
        if (assignment is null)
        {
            job.Status = DownloadJobStatus.Failed;
            job.Error = "assignment no longer exists";

            return;
        }

        IReadOnlyCollection<Acceptance> acceptances =
            await uow.AcceptanceRepository.GetByAssignmentWithStudentAsync(job.AssignmentId);
        if (acceptances.Count > settings.MaxRepos)
        {
            job.Status = DownloadJobStatus.Failed;
            job.Error = $"too many submissions ({acceptances.Count} > {settings.MaxRepos})";

            return;
        }

        Directory.CreateDirectory(workDir);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var notes = new StringBuilder();
        long totalBytes = 0;
        int exported = 0;

        foreach (Acceptance acceptance in acceptances)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            string folder = DownloadFolderNamer.Unique(
                DownloadFolderNamer.Base(acceptance.Student.LastName, acceptance.Student.FirstName,
                                          acceptance.Student.StudentId), usedNames);

            if (string.IsNullOrWhiteSpace(acceptance.RepoUrl))
            {
                notes.AppendLine($"{folder}: no repository to export");

                continue;
            }

            SnapshotResolution snapshot = await resolver.ResolveAsync(acceptance, job.Mode, assignment.Deadline);
            string target = Path.Combine(workDir, folder);

            var export = await git.ExportRepositoryAsync(acceptance.RepoUrl, target, snapshot.Sha, cancellationToken);
            if (export.Failure is { } exportFailed)
            {
                notes.AppendLine($"{folder}: export failed ({exportFailed.Reason})");

                continue;
            }

            exported++;
            if (snapshot.SeededFallback)
            {
                notes.AppendLine($"{folder}: no push before deadline, seeded state exported");
            }

            totalBytes += DirectorySize(target);
            if (totalBytes > settings.MaxTotalSizeBytes)
            {
                job.Status = DownloadJobStatus.Failed;
                job.Error = $"export exceeded the maximum size of {settings.MaxTotalSizeBytes} bytes";

                return;
            }
        }

        string artifactPath = PackageArtifact(job, settings, workDir);

        job.Status = DownloadJobStatus.Ready;
        job.ArtifactPath = artifactPath;
        job.ExpiresAt = clock.GetCurrentInstant().Plus(Duration.FromHours(settings.ArtifactTtlHours));
        job.Notes = notes.Length > 0 ? notes.ToString().TrimEnd() : null;
        logger.LogInformation("Download job {JobId} ready: {Count} repositories packaged", job.Id, exported);
    }

    private static string PackageArtifact(DownloadJob job, DownloadSettings settings, string workDir)
    {
        Directory.CreateDirectory(settings.ArtifactStoragePath);
        string artifactPath = Path.Combine(settings.ArtifactStoragePath, $"assignment-{job.AssignmentId}-job-{job.Id}.zip");
        if (File.Exists(artifactPath))
        {
            File.Delete(artifactPath);
        }
        ZipFile.CreateFromDirectory(workDir, artifactPath, CompressionLevel.Fastest, includeBaseDirectory: false);

        return artifactPath;
    }

    private static long DirectorySize(string path) =>
        new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not remove download work directory {Path}", path);
        }
    }
}

internal sealed class DownloadWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DownloadSettings> options,
    ILogger<DownloadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, options.Value.DrainSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // One scope per job and **deliberately no explicit transaction**: a download clones every
                // student repository, which runs for minutes. Holding a transaction across that would pin a
                // database connection for the whole clone. The job row's status is the unit of recovery.
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IDownloadJobProcessor processor = scope.ServiceProvider.GetRequiredService<IDownloadJobProcessor>();
                while (await processor.ProcessNextAsync(stoppingToken))
                {
                    // Drain all pending jobs queued before this tick.
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Download drain failed");
            }

            try
            {
                await Task.Delay(period, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
