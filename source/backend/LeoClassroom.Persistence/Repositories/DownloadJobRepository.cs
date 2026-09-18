using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface IDownloadJobRepository
{
    public void Add(DownloadJob job);
    public ValueTask<DownloadJob?> GetByIdAsync(long id);
    public ValueTask<DownloadJob?> GetTrackedByIdAsync(long id);
    public ValueTask<DownloadJob?> GetNextPendingAsync();
    public ValueTask<IReadOnlyCollection<DownloadJob>> GetExpiredAsync(Instant now);
}

internal sealed class DownloadJobRepository(DbSet<DownloadJob> jobs) : IDownloadJobRepository
{
    public void Add(DownloadJob job) => jobs.Add(job);

    public async ValueTask<DownloadJob?> GetByIdAsync(long id) =>
        await jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);

    public async ValueTask<DownloadJob?> GetTrackedByIdAsync(long id) =>
        await jobs.FirstOrDefaultAsync(j => j.Id == id);

    public async ValueTask<DownloadJob?> GetNextPendingAsync() =>
        await jobs.Where(j => j.Status == DownloadJobStatus.Pending)
                  .OrderBy(j => j.CreatedAt)
                  .FirstOrDefaultAsync();

    public async ValueTask<IReadOnlyCollection<DownloadJob>> GetExpiredAsync(Instant now)
    {
        List<DownloadJob> rows = await jobs
            .Where(j => j.Status == DownloadJobStatus.Ready && j.ArtifactPath != null
                        && j.ExpiresAt != null && j.ExpiresAt <= now)
            .ToListAsync();

        return rows.AsReadOnly();
    }
}
