using FluentValidation;
using LeoClassroom.Endpoints;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Util;
using LeoClassroom.Util;
using Serilog;

namespace LeoClassroom;

public static class Setup
{
    public const string CorsPolicyName = "DefaultCorsPolicy";

    /// <summary>
    ///     Aligns FluentValidation with the JSON contract
    /// </summary>
    public static void ConfigureValidation()
    {
        ValidatorOptions.Global.PropertyNameResolver = (_, member, _) => member is null
            ? null
            : ValidationNaming.ToPropertyName(member.Name);

        ValidatorOptions.Global.DisplayNameResolver = (_, member, _) => member is null
            ? null
            : ValidationNaming.ToDisplayName(member.Name);
    }

    extension(IEndpointRouteBuilder app)
    {
        /// <summary>
        ///     Maps the endpoints of every resource of this API.
        ///     Add one call per resource here - no reflection!
        /// </summary>
        public void MapApplicationEndpoints()
        {
            app.MapSystemEndpoints();
            app.MapCourseEndpoints();
            app.MapCourseTeacherEndpoints();
            app.MapMoodleLinkEndpoints();
            app.MapRosterEndpoints();
            app.MapUserEndpoints();
            app.MapAssignmentEndpoints();
            app.MapStudentAssignmentEndpoints();
            app.MapDownloadEndpoints();
            app.MapNotificationEndpoints();
            app.MapMeEndpoints();
            app.MapAuditEndpoints();
            app.MapLdapSyncEndpoints();
            app.MapForgejoHealthEndpoints();
            app.MapAdminUserEndpoints();
            app.MapWebhookEndpoints();
        }
    }

    extension(IServiceCollection services)
    {
        public void AddApplicationServices(IConfigurationManager configurationManager, bool isDev)
        {
            services.ConfigurePersistence(configurationManager, isDev);
            services.ConfigureCore(configurationManager);
        }

        public Settings LoadAndConfigureSettings(IConfigurationManager configurationManager)
        {
            var configSection = configurationManager.GetSection(Settings.SectionKey);

            services.Configure<Settings>(s => configSection.Bind(s));

            // different instance, but the same values - used for startup config outside of DI context
            var settings = Activator.CreateInstance<Settings>();
            configSection.Bind(settings);

            return settings;
        }

        public void AddCors(Settings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ClientOrigin))
            {
                throw new InvalidOperationException("Client origin has to be configured");
            }

            services.AddCors(o => o.AddPolicy(CorsPolicyName, builder =>
            {
                builder.WithOrigins(settings.ClientOrigin)
                       .AllowAnyHeader()
                       .AllowAnyMethod()
                       .AllowCredentials();
            }));

            Log.Logger.Debug("Added CORS policy with client origin {ClientOrigin}", settings.ClientOrigin);
        }

        /// <summary>
        ///     Registers the problem details services, so that unhandled exceptions are answered with the same
        ///     error shape as failed validations
        /// </summary>
        public void ConfigureProblemDetails()
        {
            services.AddProblemDetails();
            services.AddExceptionHandler<GlobalExceptionHandler>();
        }

        public void ConfigureAdditionalRouteConstraints()
        {
            services.Configure<RouteOptions>(options =>
            {
                options.ConstraintMap.Add(nameof(LocalDate), typeof(LocalDateRouteConstraint));
            });
        }

        /// <summary>
        ///     Refuses to serve traffic against a database the migrator has not brought up to date yet
        /// </summary>
        public void ConfigureSchemaGuard(IConfigurationManager configurationManager)
        {
            if (configurationManager.GetValue("Startup:VerifySchemaOnStartup", true))
            {
                services.AddHostedService<SchemaGuard>();
            }
        }

        public void ConfigureForgejoSettings(IConfigurationManager configurationManager)
        {
            services.Configure<ForgejoSettings>(
                s => configurationManager.GetSection(ForgejoSettings.SectionKey).Bind(s));
            services.Configure<ArchiveLimits>(
                s => configurationManager.GetSection(ArchiveLimits.SectionKey).Bind(s));
            services.Configure<LdapSettings>(
                s => configurationManager.GetSection(LdapSettings.SectionKey).Bind(s));
        }
    }

    private static bool IsUsableMasterKey(string? masterKey)
    {
        Span<byte> key = stackalloc byte[64];

        return Convert.TryFromBase64String(masterKey ?? string.Empty, key, out int written)
               && written is 16 or 24 or 32;
    }

    extension(WebApplicationBuilder builder)
    {
        public void AddLogging()
        {
            builder.Logging.ClearProviders();
            builder.Host.UseSerilog((_, _, config) =>
            {
                config
                    .ReadFrom.Configuration(builder.Configuration)
                    .Enrich.FromLogContext()
                    .ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
            });
        }

        /// <summary>
        ///     Refuses to start outside development when a secret is still at its empty placeholder value
        /// </summary>
        /// <remarks>
        ///     appsettings.json ships these blank on purpose - they are meant to arrive as mounted files. Booting
        ///     anyway would leave the API running with an empty webhook HMAC key and an unusable token protector,
        ///     which fails silently at the worst possible moment.
        /// </remarks>
        public void VerifyRequiredSecrets()
        {
            if (builder.Environment.IsDevelopment())
            {
                return;
            }

            string[] required =
            [
                "Forgejo:AdminToken",
                "Forgejo:WebhookSecret",
                "DataProtection:MasterKey"
            ];

            List<string> missing = [.. required.Where(key => string.IsNullOrWhiteSpace(builder.Configuration[key]))];
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Required secrets are not configured: {string.Join(", ", missing)}. " +
                    "Mount them into /run/secrets or provide them through configuration.");
            }

            if (!IsUsableMasterKey(builder.Configuration["DataProtection:MasterKey"]))
            {
                throw new InvalidOperationException(
                    "DataProtection:MasterKey must be a base64 encoded 128/192/256-bit key");
            }
        }

        public void AddFileMountedSecrets()
        {
            const string secretsDirectory = "/run/secrets";
            if (Directory.Exists(secretsDirectory))
            {
                builder.Configuration.AddKeyPerFile(secretsDirectory, optional: true);
            }
        }
    }
}
