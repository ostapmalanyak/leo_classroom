using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Notifications;

public interface INotificationDispatcher
{
    public ValueTask DispatchPendingAsync(CancellationToken cancellationToken);
}

internal sealed class NotificationDispatcher(
    IUnitOfWork uow,
    IEmailSender sender,
    IClock clock,
    IOptions<SmtpSettings> options,
    ILogger<NotificationDispatcher> logger) : INotificationDispatcher
{
    public async ValueTask DispatchPendingAsync(CancellationToken cancellationToken)
    {
        SmtpSettings settings = options.Value;
        IReadOnlyCollection<Notification> batch =
            await uow.NotificationRepository.GetSendableAsync(clock.GetCurrentInstant(), settings.BatchSize);

        foreach (Notification notification in batch)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            OneOf<Success, EmailError> sent =
                await sender.SendAsync(EmailTemplates.Render(notification, settings.AppBaseUrl), cancellationToken);
            notification.Attempts++;
            sent.Switch(
                success =>
                {
                    notification.Status = NotificationStatus.Sent;
                    notification.SentAt = clock.GetCurrentInstant();
                    notification.LastError = null;
                },
                error => ApplyFailure(notification, error.Reason, settings));

            // Saved per notification, and **deliberately not inside one transaction for the batch**: the
            // mail has already left the building by this point, and rolling the row back would send it again
            // on the next pass. One row, one save, one delivery.
            await uow.SaveChangesAsync();
        }
    }

    private void ApplyFailure(Notification notification, string error, SmtpSettings settings)
    {
        notification.LastError = error;
        if (notification.Attempts >= settings.MaxAttempts)
        {
            notification.Status = NotificationStatus.Failed;
            logger.LogWarning("Notification {Id} dead-lettered after {Attempts} attempts: {Error}",
                              notification.Id, notification.Attempts, error);

            return;
        }

        long backoffSeconds = settings.BaseBackoffSeconds * (long)Math.Pow(2, notification.Attempts - 1);
        notification.NextAttemptAt = clock.GetCurrentInstant().Plus(Duration.FromSeconds(backoffSeconds));
    }
}

internal sealed class NotificationWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SmtpSettings> options,
    ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var period = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                INotificationDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                await dispatcher.DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Notification outbox drain failed");
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
