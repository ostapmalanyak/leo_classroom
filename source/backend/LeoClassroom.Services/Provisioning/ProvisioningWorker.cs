using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.Provisioning;

internal sealed class ProvisioningWorker(
    IProvisioningQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<ForgejoSettings> settings,
    ILogger<ProvisioningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ResweepStuckAsync(stoppingToken);

        await foreach (long acceptanceId in queue.DequeueAllAsync(stoppingToken))
        {
            await ProcessAsync(acceptanceId);
        }
    }

    /// <remarks>
    ///     One scope per acceptance, and **deliberately no explicit transaction**: provisioning a repository
    ///     is a sequence of Forgejo calls - fork or migrate, collaborators, branches - and a transaction held
    ///     across them would keep a database connection and its locks open for the length of every one. The
    ///     acceptance row carries the status instead, so a failure is visible rather than rolled back; the
    ///     startup sweep below processes anything left in <c>Provisioning</c> after an interrupted run.
    /// </remarks>
    private async Task ProcessAsync(long acceptanceId)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IRepoProvisioningService provisioning = scope.ServiceProvider.GetRequiredService<IRepoProvisioningService>();
            await provisioning.ProvisionAcceptanceAsync(acceptanceId);
        }
        catch (Exception ex)
        {
            logger.LogError("Unhandled exception provisioning acceptance {AcceptanceId}: {ExceptionType}: {Exception}",
                            acceptanceId, ex.GetType().Name, Redact(ex.ToString()));
            await RecordFailureAsync(acceptanceId, Redact(ex.Message));
        }
    }

    private async Task RecordFailureAsync(long acceptanceId, string reason)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var acceptance = await uow.AcceptanceRepository.GetTrackedByIdAsync(acceptanceId);
            if (acceptance is null)
            {
                logger.LogError("Could not record provisioning failure for missing acceptance {AcceptanceId}",
                                acceptanceId);

                return;
            }

            acceptance.Status = SubmissionStatus.Failed;
            acceptance.FailureReason = reason.Length == 0 ? "Unexpected provisioning error" : reason;
            await uow.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError("Could not save provisioning failure for acceptance {AcceptanceId}: {ExceptionType}: {Exception}",
                            acceptanceId, ex.GetType().Name, Redact(ex.ToString()));
        }
    }

    private string Redact(string value)
    {
        string secret = settings.Value.AdminToken;

        return string.IsNullOrEmpty(secret)
            ? value
            : value.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
    }

    private async Task ResweepStuckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            IReadOnlyCollection<long> stuck =
                await uow.AcceptanceRepository.GetIdsByStatusAsync(SubmissionStatus.Provisioning);
            foreach (long id in stuck)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // This worker is the queue's only reader. Filling its bounded queue before starting to
                // drain it deadlocks startup when there are more stuck acceptances than queue slots.
                await ProcessAsync(id);
            }
            if (stuck.Count > 0)
            {
                logger.LogInformation("Processed {Count} stuck provisioning submissions on startup", stuck.Count);
            }
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Startup provisioning sweep failed: {ExceptionType}: {Exception}",
                            ex.GetType().Name, Redact(ex.ToString()));
        }
    }
}
