using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Download;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Ldap;
using LeoClassroom.Services.Moodle;
using LeoClassroom.Services.Notifications;
using LeoClassroom.Services.Provisioning;
using LeoClassroom.Services.Scheduling;
using LeoClassroom.Services.Security;
using LeoClassroom.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.Util;

public static class ServicesSetup
{
    public static void ConfigureCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock>(SystemClock.Instance);

        // singleton on purpose: it is shared by every scoped provisioning service, and primed once at boot
        services.AddSingleton<IUserProvisioningCache, UserProvisioningCache>();
        services.AddHostedService<UserProvisioningPrimer>();

        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<IUserProvisioningService, UserProvisioningService>();
        services.AddScoped<IDeletionCascade, DeletionCascade>();
        services.AddScoped<IUserDeletionService, UserDeletionService>();
        services.AddScoped<IGitCredentialService, GitCredentialService>();
        services.AddScoped<IForgejoDiagnosticsService, ForgejoDiagnosticsService>();
        services.AddScoped<ICustomRosterService, CustomRosterService>();
        services.AddScoped<ICourseTeacherService, CourseTeacherService>();
        services.AddScoped<IUserDirectoryService, UserDirectoryService>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<IStudentAssignmentService, StudentAssignmentService>();
        services.AddScoped<IAssignmentAutoDeleteService, AssignmentAutoDeleteService>();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
        services.AddScoped<IAuditRetentionService, AuditRetentionService>();
        services.Configure<AuditSettings>(configuration.GetSection(AuditSettings.SectionKey));

        services.ConfigureForgejo();
        services.ConfigureLdap();
        services.ConfigureNotifications(configuration);
        services.ConfigureMoodle(configuration);
        services.ConfigureDownload(configuration);
        services.ConfigureScheduling(configuration);
    }

    private static void ConfigureDownload(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DownloadSettings>(configuration.GetSection(DownloadSettings.SectionKey));
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddScoped<ISubmissionSnapshotResolver, SubmissionSnapshotResolver>();
        services.AddScoped<IDownloadJobProcessor, DownloadJobProcessor>();
        services.AddScoped<IDownloadCleanupService, DownloadCleanupService>();
        services.AddHostedService<DownloadWorker>();
    }

    private static void ConfigureMoodle(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MoodleSettings>(configuration.GetSection(MoodleSettings.SectionKey));
        services.Configure<DataProtectionSettings>(configuration.GetSection(DataProtectionSettings.SectionKey));
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddHttpClient("moodle");
        services.AddScoped<IMoodleClient, MoodleClient>();
        services.AddScoped<IMoodleLinkService, MoodleLinkService>();
        services.AddScoped<IMoodleSyncService, MoodleSyncService>();
        services.AddScoped<IMoodleSyncDispatcher, MoodleSyncDispatcher>();
        services.AddScoped<IMoodleBackfillService, MoodleBackfillService>();
        services.AddHostedService<MoodleSyncWorker>();
    }

    private static void ConfigureNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpSettings>(configuration.GetSection(SmtpSettings.SectionKey));
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddHostedService<NotificationWorker>();
    }

    private static void ConfigureLdap(this IServiceCollection services)
    {
        services.AddScoped<ILdapDirectory, LdapDirectory>();
        services.AddScoped<ILdapSyncService, LdapSyncService>();
    }

    private static void ConfigureForgejo(this IServiceCollection services)
    {
        services.AddTransient<ForgejoAuthHandler>();
        services.AddHttpClient<IForgejoClient, ForgejoClient>((sp, client) =>
                {
                    ForgejoSettings settings = sp.GetRequiredService<IOptions<ForgejoSettings>>().Value;
                    client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
                })
                .AddHttpMessageHandler<ForgejoAuthHandler>();

        services.AddSingleton<IArchiveExtractor, ArchiveExtractor>();
        services.AddScoped<IGitService, GitService>();
        services.AddScoped<IReconciliationService, ReconciliationService>();
        services.AddScoped<IRepoProvisioningService, RepoProvisioningService>();
        services.AddSingleton<IProvisioningQueue, ProvisioningQueue>();
        services.AddHostedService<ProvisioningWorker>();
        services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
        services.AddScoped<IWebhookService, WebhookService>();
        services.AddScoped<IWebhookConsumer, PushActivityConsumer>();
        services.AddScoped<IWebhookConsumer, LateFlaggingConsumer>();
        services.AddScoped<IWebhookConsumer, CommitAnalyticsConsumer>();
        services.AddScoped<IWebhookConsumer, FeedbackReviewConsumer>();
        services.AddScoped<ISubmissionReviewService, SubmissionReviewService>();
    }
}
