namespace LeoClassroom.Persistence.Util;

/// <summary>
///     Runs a unit of work inside one explicit transaction
/// </summary>
/// <remarks>
///     <para>
///         The convention in this application is that the **entry point** owns the transaction - an endpoint,
///         the provisioning middleware, or a scheduled job - and the services it calls simply save. Services
///         may call <c>SaveChangesAsync</c> as often as they need to: those saves all enlist in the
///         transaction the entry point opened, so the whole run commits or rolls back together.
///     </para>
///     <para>
///         Without an explicit transaction, EF gives each <c>SaveChangesAsync</c> an implicit one of its own,
///         and a run that fails halfway leaves the earlier saves committed.
///     </para>
/// </remarks>
public static class TransactionProviderExtensions
{
    public static async Task ExecuteAsync(this ITransactionProvider transaction, Func<Task> work)
    {
        await transaction.BeginTransactionAsync();
        try
        {
            await work();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();

            throw;
        }
    }

    public static async Task<T> ExecuteAsync<T>(this ITransactionProvider transaction, Func<Task<T>> work)
    {
        await transaction.BeginTransactionAsync();
        try
        {
            T result = await work();
            await transaction.CommitAsync();

            return result;
        }
        catch
        {
            await transaction.RollbackAsync();

            throw;
        }
    }
}
