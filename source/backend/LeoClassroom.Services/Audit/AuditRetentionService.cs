using LeoClassroom.Services.Util;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.Audit;

public interface IAuditRetentionService
{
    public ValueTask<int> PurgeAsync();
}

internal sealed class AuditRetentionService(
    IUnitOfWork uow, IAuditLog audit, IClock clock, IOptions<AuditSettings> settings,
    ILogger<AuditRetentionService> logger) : IAuditRetentionService
{
    public async ValueTask<int> PurgeAsync()
    {
        Instant cutoff = clock.GetCurrentInstant().Minus(Duration.FromDays(365 * settings.Value.RetentionYears));
        int purged = await uow.AuditEventRepository.PurgeOlderThanAsync(cutoff);
        if (purged > 0)
        {
            logger.LogInformation("Purged {Count} audit events older than {Cutoff}", purged, cutoff);
            await audit.RecordAsync(AuditAction.RetentionPurge, "AuditEvent", null,
                                    new Dictionary<string, string>
                                    {
                                        ["purgedCount"] = purged.ToString(),
                                        ["cutoff"] = cutoff.ToString()
                                    });
        }

        return purged;
    }
}
