using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services.Download;

public interface IDownloadCleanupService
{
    public ValueTask CleanupAsync();
}

internal sealed class DownloadCleanupService(
    IUnitOfWork uow, IClock clock, ILogger<DownloadCleanupService> logger) : IDownloadCleanupService
{
    public async ValueTask CleanupAsync()
    {
        IReadOnlyCollection<DownloadJob> expired =
            await uow.DownloadJobRepository.GetExpiredAsync(clock.GetCurrentInstant());

        foreach (DownloadJob job in expired)
        {
            TryDeleteArtifact(job.ArtifactPath);
            job.ArtifactPath = null;
        }

        if (expired.Count > 0)
        {
            await uow.SaveChangesAsync();
            logger.LogInformation("Removed {Count} expired download artifacts", expired.Count);
        }
    }

    private void TryDeleteArtifact(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not delete expired download artifact {Path}", path);
        }
    }
}
