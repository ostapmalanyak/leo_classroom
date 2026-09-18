using LeoClassroom.Persistence.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LeoClassroom.TestInt.Util;

// ReSharper disable once ClassNeverInstantiated.Global - Instantiated by xUnit
public sealed class WebApiTestFixture : IAsyncLifetime
{
    // pinned to the image compose.yaml runs, so the tests exercise the server the application actually gets
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:18-alpine")
                                                              .WithDatabase("public")
                                                              .WithUsername("postgres")
                                                              .WithPassword("postgres")
                                                              .Build();

    public HttpClient Client
    {
        get => field ?? throw new InvalidOperationException("Client not created");
        private set;
    }

    public IClock Clock
    {
        get => field ?? throw new InvalidOperationException("Clock not created");
        private set;
    }

    public TestAuthState AuthState => Factory.AuthState;

    private WebAppFactory Factory
    {
        get => field ?? throw new InvalidOperationException("Factory not created");
        set;
    }

    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAsync();

        Factory = new WebAppFactory(_postgresContainer.GetConnectionString());
        Client = Factory.CreateClient();
        Clock = Factory.TestClock;
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        await _postgresContainer.StopAsync();
        await _postgresContainer.DisposeAsync();
    }

    public async ValueTask RestoreDatabaseAsync(Func<DatabaseContext, ValueTask> seedDataImporter)
    {
        await using var contextScope = CreateContextScope();

        await contextScope.Context.Database
                          .ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{DatabaseContext.SchemaName}\" CASCADE;");
        await contextScope.Context.Database.MigrateAsync();

        await seedDataImporter(contextScope.Context);
    }

    public async ValueTask ModifyDatabaseContentAsync(Func<DatabaseContext, ValueTask> modifier)
    {
        await using var contextScope = CreateContextScope();

        await modifier(contextScope.Context);
    }

    private ContextScope CreateContextScope() => new(Factory.Services);

    private sealed class ContextScope : IAsyncDisposable
    {
        private readonly IServiceScope _scope;

        public ContextScope(IServiceProvider serviceProvider)
        {
            _scope = serviceProvider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<DatabaseContext>();
        }

        public DatabaseContext Context { get; }

        public async ValueTask DisposeAsync()
        {
            if (_scope is IAsyncDisposable scopeAsyncDisposable)
            {
                await scopeAsyncDisposable.DisposeAsync();
            }
            else
            {
                _scope.Dispose();
            }

            await Context.DisposeAsync();
        }
    }
}
