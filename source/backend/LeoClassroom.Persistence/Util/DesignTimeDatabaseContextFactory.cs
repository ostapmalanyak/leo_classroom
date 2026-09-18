using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeoClassroom.Persistence.Util;

/// <summary>
///     Builds a <see cref="DatabaseContext" /> for the EF tooling without starting the application
/// </summary>
/// <remarks>
///     <para>
///         Without this, <c>dotnet ef</c> and the migration bundle fall back to running the web application's
///         host to obtain the context. That drags the entire API configuration - CORS origin, Keycloak
///         authority, the secret guard - into a job whose only concern is the schema, and it fails outright in
///         the migrator image, which ships the bundle alone with no <c>appsettings.json</c> beside it.
///     </para>
///     <para>
///         The connection string is only needed so the provider can be configured; <c>efbundle --connection</c>
///         and <c>dotnet ef --connection</c> override it before anything is executed. It is read from the
///         environment so the migrator can supply the real one, with a local development fallback.
///     </para>
/// </remarks>
internal sealed class DesignTimeDatabaseContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
{
    private const string ConnectionVariable = "ConnectionStrings__Postgres";

    private const string DevelopmentFallback =
        "Host=localhost;Database=postgres;Username=postgres;Password=postgres";

    public DatabaseContext CreateDbContext(string[] args)
    {
        string connectionString = Environment.GetEnvironmentVariable(ConnectionVariable) ?? DevelopmentFallback;

        var optionsBuilder = new DbContextOptionsBuilder<DatabaseContext>();

        // the same configuration the application uses, so the migrations history table and the NodaTime
        // mapping match what runs in production
        PersistenceSetup.ConfigureDatabaseContextOptions(optionsBuilder, connectionString,
                                                         sensitiveDataLogging: false);

        return new DatabaseContext(optionsBuilder.Options);
    }
}
