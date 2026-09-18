using LeoClassroom.Persistence.Model;
using LeoClassroom.Shared;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface INotificationRepository
{
    public void Add(Notification notification);
    public ValueTask<bool> ExistsAsync(string eventKey, long recipientUserId);
    public ValueTask<IReadOnlyCollection<NotificationRecipient>> GetRecipientsAsync(
        long assignmentId, NotificationTrigger trigger);
    public ValueTask<IReadOnlyCollection<Notification>> GetSendableAsync(Instant now, int batchSize);
    public ValueTask<int> DeleteByRecipientAsync(long userId);
}

internal sealed class NotificationRepository(
    DbSet<Notification> notifications,
    DbSet<Assignment> assignments,
    DbSet<NotificationPreference> preferences) : INotificationRepository
{
    public void Add(Notification notification) => notifications.Add(notification);

    public async ValueTask<int> DeleteByRecipientAsync(long userId) =>
        await notifications.Where(n => n.RecipientUserId == userId).ExecuteDeleteAsync();

    public async ValueTask<bool> ExistsAsync(string eventKey, long recipientUserId) =>
        await notifications.AsNoTracking()
                           .AnyAsync(n => n.EventKey == eventKey && n.RecipientUserId == recipientUserId);

    public async ValueTask<IReadOnlyCollection<NotificationRecipient>> GetRecipientsAsync(
        long assignmentId, NotificationTrigger trigger)
    {
        IQueryable<User> members = assignments.AsNoTracking()
            .Where(a => a.Id == assignmentId)
            .SelectMany(a => a.Course.Roster.Members)
            .Where(m => m.Role == Role.Student && m.State == UserState.Active && m.Email != null);

        IQueryable<NotificationPreference> opted = trigger == NotificationTrigger.NewAssignment
            ? preferences.AsNoTracking().Where(p => p.NewAssignment)
            : preferences.AsNoTracking().Where(p => p.DeadlineChanged);

        List<NotificationRecipient> recipients = await
            (from m in members
             join p in opted on m.Id equals p.UserId
             select new NotificationRecipient(m.Id, m.Email!)).ToListAsync();

        return recipients.AsReadOnly();
    }

    public async ValueTask<IReadOnlyCollection<Notification>> GetSendableAsync(Instant now, int batchSize)
    {
        List<Notification> rows = await notifications
            .Where(n => n.Status == NotificationStatus.Pending && n.NextAttemptAt <= now)
            .OrderBy(n => n.NextAttemptAt)
            .Take(batchSize)
            .ToListAsync();

        return rows.AsReadOnly();
    }
}
