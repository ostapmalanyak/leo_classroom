using LeoClassroom.Services.Security;
using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Moodle;

public interface IMoodleSyncDispatcher
{
    public ValueTask DispatchPendingAsync(CancellationToken cancellationToken);
}

internal sealed class MoodleSyncDispatcher(
    IUnitOfWork uow,
    IMoodleClient client,
    ISecretProtector protector,
    IClock clock,
    IOptions<MoodleSettings> options,
    ILogger<MoodleSyncDispatcher> logger) : IMoodleSyncDispatcher
{
    public async ValueTask DispatchPendingAsync(CancellationToken cancellationToken)
    {
        MoodleSettings settings = options.Value;
        IReadOnlyCollection<MoodleSyncOp> batch =
            await uow.MoodleSyncRepository.GetSendableAsync(clock.GetCurrentInstant(), settings.BatchSize);

        foreach (MoodleSyncOp op in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            OneOf<Success, MoodleError> result = await ProcessAsync(op, settings);
            result.Switch(
                success =>
                {
                    op.Status = MoodleSyncStatus.Sent;
                    op.LastError = null;
                },
                error =>
                {
                    op.Attempts++;
                    ApplyFailure(op, error.Reason, settings);
                });

                        // Per operation, and **deliberately not one transaction for the batch**: each row is a call to
            // Moodle that has already happened by the time its outcome is written. The outbox row is the unit
            // of recovery - a failed one is retried with backoff, not rolled back.
            await uow.SaveChangesAsync();
        }
    }

    private async ValueTask<OneOf<Success, MoodleError>> ProcessAsync(MoodleSyncOp op, MoodleSettings settings)
    {
        MoodleLink? link = await uow.MoodleLinkRepository.GetByCourseIdAsync(op.CourseId);
        if (link is null || !link.Enabled || link.TokenCipher is null)
        {
            // Link removed/disabled after the op was queued — nothing to push.
            return new Success();
        }

        if (!protector.TryUnprotect(link.TokenCipher, out string token))
        {
            return new MoodleError("could not decrypt Moodle token");
        }

        var target = new MoodleTarget(link.MoodleBaseUrl, token, link.MoodleCourseId);
        MoodleItemMapping? mapping = await uow.MoodleSyncRepository.GetMappingByAssignmentAsync(op.AssignmentId);

        return op.OpType switch
        {
            MoodleSyncOpType.Delete => await DeleteAsync(target, mapping),
            _ => await UpsertAsync(op, settings, target, mapping)
        };
    }

    private async ValueTask<OneOf<Success, MoodleError>> UpsertAsync(
        MoodleSyncOp op, MoodleSettings settings, MoodleTarget target, MoodleItemMapping? mapping)
    {
        var payload = new MoodleItemPayload(
            op.Title ?? string.Empty, op.Deadline,
            $"{settings.AppBaseUrl.TrimEnd('/')}/my-assignments/{op.AssignmentId}");

        if (mapping is not null)
        {
            return await client.UpdateItemAsync(target, mapping.MoodleItemId, payload);
        }

        OneOf<Success<long>, MoodleError> created = await client.CreateItemAsync(target, payload);

        return created.Match<OneOf<Success, MoodleError>>(
            item =>
            {
                uow.MoodleSyncRepository.AddMapping(new MoodleItemMapping
                {
                    AssignmentId = op.AssignmentId, CourseId = op.CourseId, MoodleItemId = item.Value
                });

                return new Success();
            },
            error => error);
    }

    private async ValueTask<OneOf<Success, MoodleError>> DeleteAsync(MoodleTarget target, MoodleItemMapping? mapping)
    {
        if (mapping is null)
        {
            return new Success();
        }

        OneOf<Success, MoodleError> deleted = await client.DeleteItemAsync(target, mapping.MoodleItemId);
        if (deleted.Failure is { } deleteFailed)
        {
            return deleteFailed;
        }

        uow.MoodleSyncRepository.RemoveMapping(mapping);

        return new Success();
    }

    private void ApplyFailure(MoodleSyncOp op, string error, MoodleSettings settings)
    {
        op.LastError = error;
        if (op.Attempts >= settings.MaxAttempts)
        {
            op.Status = MoodleSyncStatus.Failed;
            logger.LogWarning("Moodle sync op {Id} dead-lettered after {Attempts} attempts: {Error}",
                              op.Id, op.Attempts, error);

            return;
        }

        long backoffSeconds = settings.BaseBackoffSeconds * (long)Math.Pow(2, op.Attempts - 1);
        op.NextAttemptAt = clock.GetCurrentInstant().Plus(Duration.FromSeconds(backoffSeconds));
    }
}

internal sealed class MoodleSyncWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MoodleSettings> options,
    ILogger<MoodleSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, options.Value.OutboxDrainSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                IMoodleSyncDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<IMoodleSyncDispatcher>();
                await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Moodle sync drain failed");
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
