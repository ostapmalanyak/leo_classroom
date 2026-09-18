using LeoClassroom.Persistence.Util;
using Microsoft.EntityFrameworkCore;

namespace LeoClassroom.Util;

// Fails the backend fast at boot when the live schema is not the version this build expects, so it never
// serves traffic against an unmigrated database. The migrator applies the EF bundle before the backend starts.
internal sealed class SchemaGuard(IServiceScopeFactory scopeFactory, ILogger<SchemaGuard> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        DatabaseContext context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        List<string> pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"Database schema is out of date: {pending.Count} migration(s) pending " +
                $"({string.Join(", ", pending)}). Run the migrator before starting the backend.");
        }

        logger.LogInformation("Database schema verified up to date");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
