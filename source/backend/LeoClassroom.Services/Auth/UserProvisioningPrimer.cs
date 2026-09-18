using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LeoClassroom.Services.Auth;

/// <summary>
///     Fills <see cref="IUserProvisioningCache" /> from the user table at boot, so that the first request of every
///     already known caller is served without a database round-trip
/// </summary>
/// <remarks>
///     A failure here is not fatal: an empty cache is simply filled lazily, one round-trip per caller, which is the
///     behaviour this priming exists to avoid rather than something the application depends on. Booting is the job
///     of <c>SchemaGuard</c>.
/// </remarks>
internal sealed class UserProvisioningPrimer(
    IServiceScopeFactory scopeFactory,
    IUserProvisioningCache cache,
    ILogger<UserProvisioningPrimer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // No transaction: this reads the user table to fill the cache and writes nothing.
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IUnitOfWork uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            cache.Refresh(await uow.UserRepository.GetAllProvisionedAsync());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not prime the provisioning cache; it will fill as callers arrive");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
