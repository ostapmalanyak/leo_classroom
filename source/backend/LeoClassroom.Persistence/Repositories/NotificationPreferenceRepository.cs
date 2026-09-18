using LeoClassroom.Persistence.Model;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Persistence.Repositories;

public interface INotificationPreferenceRepository
{
    public void Add(NotificationPreference preference);
    public ValueTask<NotificationPreference?> GetByUserIdAsync(long userId);
    public ValueTask<NotificationPreference?> GetTrackedByUserIdAsync(long userId);
    public ValueTask<int> DeleteByUserAsync(long userId);
}

internal sealed class NotificationPreferenceRepository(DbSet<NotificationPreference> preferences)
    : INotificationPreferenceRepository
{
    public void Add(NotificationPreference preference) => preferences.Add(preference);

    public async ValueTask<int> DeleteByUserAsync(long userId) =>
        await preferences.Where(p => p.UserId == userId).ExecuteDeleteAsync();

    public async ValueTask<NotificationPreference?> GetByUserIdAsync(long userId) =>
        await preferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);

    public async ValueTask<NotificationPreference?> GetTrackedByUserIdAsync(long userId) =>
        await preferences.FirstOrDefaultAsync(p => p.UserId == userId);
}
