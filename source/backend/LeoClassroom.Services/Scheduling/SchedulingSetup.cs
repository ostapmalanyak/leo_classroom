using LeoClassroom.Services.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace LeoClassroom.Services.Scheduling;

public static class SchedulingSetup
{
    public static void ConfigureScheduling(this IServiceCollection services, IConfiguration configuration)
    {
        string ldapCron = configuration.GetSection(LdapSettings.SectionKey)["NightlyCron"] ?? "0 0 2 * * ?";
        string auditCron = configuration.GetSection(AuditSettings.SectionKey)["RetentionCron"] ?? "0 0 3 * * ?";
        string moodleBackfillCron = configuration.GetSection(MoodleSettings.SectionKey)["BackfillCron"] ?? "0 0 4 * * ?";
        string downloadCleanupCron = configuration.GetSection(DownloadSettings.SectionKey)["CleanupCron"] ?? "0 15 * * * ?";

        services.AddQuartz(q =>
        {
            q.AddJob<DeadlineReconciliationJob>(j => j.WithIdentity(DeadlineReconciliationJob.Key));
            q.AddTrigger(t => t.ForJob(DeadlineReconciliationJob.Key)
                               .WithSimpleSchedule(s => s.WithIntervalInMinutes(5).RepeatForever()));

            q.AddJob<LdapSyncJob>(j => j.WithIdentity(LdapSyncJob.Key));
            q.AddTrigger(t => t.ForJob(LdapSyncJob.Key).WithCronSchedule(ldapCron));

            q.AddJob<AssignmentAutoDeleteJob>(j => j.WithIdentity(AssignmentAutoDeleteJob.Key));
            q.AddTrigger(t => t.ForJob(AssignmentAutoDeleteJob.Key).WithCronSchedule("0 30 2 * * ?"));

            q.AddJob<AuditRetentionJob>(j => j.WithIdentity(AuditRetentionJob.Key));
            q.AddTrigger(t => t.ForJob(AuditRetentionJob.Key).WithCronSchedule(auditCron));

            q.AddJob<MoodleBackfillJob>(j => j.WithIdentity(MoodleBackfillJob.Key));
            q.AddTrigger(t => t.ForJob(MoodleBackfillJob.Key).WithCronSchedule(moodleBackfillCron));

            q.AddJob<DownloadCleanupJob>(j => j.WithIdentity(DownloadCleanupJob.Key));
            q.AddTrigger(t => t.ForJob(DownloadCleanupJob.Key).WithCronSchedule(downloadCleanupCron));
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        // Analytics-rollup jobs are registered by later changes.
    }
}
