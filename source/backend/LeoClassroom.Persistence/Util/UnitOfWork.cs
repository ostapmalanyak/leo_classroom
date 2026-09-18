using System.Data;
using LeoClassroom.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LeoClassroom.Persistence.Util;

public interface ITransactionProvider : IAsyncDisposable, IDisposable
{
    public ValueTask BeginTransactionAsync();
    public ValueTask CommitAsync();
    public ValueTask RollbackAsync();
}

public interface IUnitOfWork
{
    public ICourseRepository CourseRepository { get; }
    public IRosterRepository RosterRepository { get; }
    public IUserRepository UserRepository { get; }
    public IAssignmentRepository AssignmentRepository { get; }
    public IAcceptanceRepository AcceptanceRepository { get; }
    public IWebhookEventRepository WebhookEventRepository { get; }
    public IAuditEventRepository AuditEventRepository { get; }
    public INotificationPreferenceRepository NotificationPreferenceRepository { get; }
    public INotificationRepository NotificationRepository { get; }
    public IMoodleLinkRepository MoodleLinkRepository { get; }
    public IMoodleSyncRepository MoodleSyncRepository { get; }
    public IDownloadJobRepository DownloadJobRepository { get; }
    public Task SaveChangesAsync();
}

internal sealed class UnitOfWork(DatabaseContext context, ILogger<UnitOfWork> logger)
    : IUnitOfWork, ITransactionProvider
{
    private IDbContextTransaction? _transaction;

    public ICourseRepository CourseRepository => new CourseRepository(context.Courses);
    public IRosterRepository RosterRepository => new RosterRepository(context.Rosters, context.Courses);
    public IUserRepository UserRepository => new UserRepository(context.Users);
    public IAssignmentRepository AssignmentRepository => new AssignmentRepository(context.Assignments);
    public IAcceptanceRepository AcceptanceRepository => new AcceptanceRepository(context.Acceptances);
    public IWebhookEventRepository WebhookEventRepository => new WebhookEventRepository(context.WebhookEvents);
    public IAuditEventRepository AuditEventRepository => new AuditEventRepository(context.AuditEvents);
    public INotificationPreferenceRepository NotificationPreferenceRepository =>
        new NotificationPreferenceRepository(context.NotificationPreferences);
    public INotificationRepository NotificationRepository =>
        new NotificationRepository(context.Notifications, context.Assignments, context.NotificationPreferences);
    public IMoodleLinkRepository MoodleLinkRepository => new MoodleLinkRepository(context.MoodleLinks);
    public IMoodleSyncRepository MoodleSyncRepository =>
        new MoodleSyncRepository(context.MoodleSyncOps, context.MoodleItemMappings, context.Assignments);
    public IDownloadJobRepository DownloadJobRepository => new DownloadJobRepository(context.DownloadJobs);

    public async ValueTask BeginTransactionAsync()
    {
        if (_transaction is not null)
        {
            throw new TransactionException("Transaction already started, unable to start another");
        }

        _transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Snapshot);
    }

    public async ValueTask CommitAsync()
    {
        if (_transaction is null)
        {
            throw new TransactionException("No transaction started, unable to commit");
        }

        await _transaction.CommitAsync();
        _transaction = null;
    }

    public async ValueTask RollbackAsync()
    {
        if (_transaction is null)
        {
            throw new TransactionException("No transaction started, unable to rollback");
        }

        await _transaction.RollbackAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is null)
        {
            return;
        }

        var transaction = _transaction;
        _transaction = null;
        await transaction.RollbackAsync();
        await transaction.DisposeAsync();
    }

    public void Dispose()
    {
        if (_transaction is null)
        {
            return;
        }

        logger.LogWarning("Transaction was not disposed asynchronously and will now be rolled back and disposed");
        _transaction.Rollback();
        _transaction.Dispose();
    }

    public Task SaveChangesAsync() => context.SaveChangesAsync();

    private sealed class TransactionException(string message) : Exception(message);
}
