using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace LeoClassroom.TestInt.Util;

internal sealed class WebAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    public const string WebhookSecret = "test-webhook-secret";

    /// <summary>The username the tests authenticate as when they need administrator rights</summary>
    public const string AdminStudentId = "IF000003";

    // A fixed 256-bit base64 key for Tier-2 secret encryption in tests.
    public const string MasterKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    public static readonly LocalDateTime CurrentDateTimeForTests = new(2026, 01, 01, 14, 30, 00);

    public TestAuthState AuthState { get; } = new();

    public IClock TestClock =>
        Services.GetService<IClock>() ??
        throw new InvalidOperationException("Service provider not available or clock not registered");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Forgejo:BaseUrl"] = "http://forgejo.test",
                ["Forgejo:AdminToken"] = "test-token",
                ["Forgejo:WebhookSecret"] = WebhookSecret,
                ["Forgejo:WebhookTargetUrl"] = "http://api.test/api/webhooks/forgejo",
                ["DataProtection:MasterKey"] = MasterKey,
                ["Moodle:OutboxDrainSeconds"] = "3600",
                ["Download:DrainSeconds"] = "3600",
                ["Download:ArtifactStoragePath"] = Path.Combine(Path.GetTempPath(), "leo-test-downloads"),
                ["Startup:VerifySchemaOnStartup"] = "false",
                // the realm has no administrator role; this application names its admins by username
                ["Keycloak:AdminUsers:0"] = AdminStudentId
            });
        });
        builder.ConfigureTestServices(services =>
        {
            SetTestDbContext(services);
            SetTestClock(services);
            SetTestAuthentication(services);
        });
    }

    private void SetTestAuthentication(IServiceCollection services)
    {
        services.AddSingleton(AuthState);
        services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthState.Scheme;
                    options.DefaultChallengeScheme = TestAuthState.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthState.Scheme, _ => { });
    }

    private void SetTestDbContext(IServiceCollection services)
    {
        RemoveServiceIfExists<DbContextOptions<DatabaseContext>>(services);

        services.AddDbContext<DatabaseContext>(options =>
            PersistenceSetup.ConfigureDatabaseContextOptions(options, connectionString, false));
    }

    private static void SetTestClock(IServiceCollection services)
    {
        RemoveServiceIfExists<IClock>(services);

        var clockMock = Substitute.For<IClock>();
        var currentInstant = CurrentDateTimeForTests.InZoneLeniently(Const.TimeZone).ToInstant();
        clockMock.GetCurrentInstant().Returns(currentInstant);

        services.AddSingleton(clockMock);
    }

    private static void RemoveServiceIfExists<TService>(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(s => s.ServiceType == typeof(TService));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }
    }
}
