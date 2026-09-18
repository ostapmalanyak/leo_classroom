using LeoClassroom.Services.Auth;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Notifications;

public interface INotificationPreferenceService
{
    public ValueTask<OneOf<NotificationPreferenceView, NotFound>> GetAsync();
    public ValueTask<OneOf<NotificationPreferenceView, NotFound>> UpdateAsync(bool newAssignment, bool deadlineChanged);
}

internal sealed class NotificationPreferenceService(IUnitOfWork uow, ICurrentUser currentUser)
    : INotificationPreferenceService
{
    public async ValueTask<OneOf<NotificationPreferenceView, NotFound>> GetAsync()
    {
        long? userId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (userId is null)
        {
            return new NotFound();
        }

        NotificationPreference? preference = await uow.NotificationPreferenceRepository.GetByUserIdAsync(userId.Value);

        return new NotificationPreferenceView(preference?.NewAssignment ?? false, preference?.DeadlineChanged ?? false);
    }

    public async ValueTask<OneOf<NotificationPreferenceView, NotFound>> UpdateAsync(
        bool newAssignment, bool deadlineChanged)
    {
        long? userId = await uow.UserRepository.GetIdByStudentIdAsync(currentUser.StudentId);
        if (userId is null)
        {
            return new NotFound();
        }

        NotificationPreference? preference =
            await uow.NotificationPreferenceRepository.GetTrackedByUserIdAsync(userId.Value);
        if (preference is null)
        {
            preference = new NotificationPreference { UserId = userId.Value };
            uow.NotificationPreferenceRepository.Add(preference);
        }

        preference.NewAssignment = newAssignment;
        preference.DeadlineChanged = deadlineChanged;
        await uow.SaveChangesAsync();

        return new NotificationPreferenceView(newAssignment, deadlineChanged);
    }
}
