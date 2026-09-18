using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.Notifications;

public interface INotificationService
{
    public ValueTask EnqueueAssignmentCreatedAsync(Assignment assignment);
    public ValueTask EnqueueDeadlineChangedAsync(Assignment assignment);
}

internal sealed class NotificationService(
    IUnitOfWork uow,
    IClock clock,
    IOptions<SmtpSettings> options,
    ILogger<NotificationService> logger) : INotificationService
{
    public ValueTask EnqueueAssignmentCreatedAsync(Assignment assignment) =>
        EnqueueAsync(assignment, NotificationTrigger.NewAssignment, $"new-assignment:{assignment.Id}");

    public ValueTask EnqueueDeadlineChangedAsync(Assignment assignment)
    {
        string deadlineKey = assignment.Deadline?.ToUnixTimeSeconds().ToString() ?? "none";

        return EnqueueAsync(assignment, NotificationTrigger.DeadlineChanged,
                            $"deadline-changed:{assignment.Id}:{deadlineKey}");
    }

    private async ValueTask EnqueueAsync(Assignment assignment, NotificationTrigger trigger, string eventKey)
    {
        IReadOnlyCollection<NotificationRecipient> recipients =
            await uow.NotificationRepository.GetRecipientsAsync(assignment.Id, trigger);
        if (recipients.Count == 0)
        {
            return;
        }

        Instant now = clock.GetCurrentInstant();
        string language = options.Value.DefaultLanguage;
        int added = 0;
        foreach (NotificationRecipient recipient in recipients)
        {
            if (await uow.NotificationRepository.ExistsAsync(eventKey, recipient.UserId))
            {
                continue;
            }

            uow.NotificationRepository.Add(new Notification
            {
                EventKey = eventKey,
                RecipientUserId = recipient.UserId,
                RecipientEmail = recipient.Email,
                Trigger = trigger,
                AssignmentId = assignment.Id,
                AssignmentTitle = assignment.Title,
                Deadline = assignment.Deadline,
                Language = language,
                Status = NotificationStatus.Pending,
                Attempts = 0,
                CreatedAt = now,
                NextAttemptAt = now
            });
            added++;
        }

        if (added > 0)
        {
            await uow.SaveChangesAsync();
            logger.LogInformation("Queued {Count} {Trigger} notifications for assignment {AssignmentId}",
                                  added, trigger, assignment.Id);
        }
    }
}
