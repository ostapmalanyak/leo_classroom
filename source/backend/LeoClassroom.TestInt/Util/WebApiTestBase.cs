using System.Text.Json;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.TestInt.Util;

public abstract class WebApiTestBase(WebApiTestFixture webApiFixture) : IClassFixture<WebApiTestFixture>, IAsyncLifetime
{
    private static readonly Lazy<JsonSerializerOptions> jsonOptions = new(() =>
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web);
        JsonConfig.ConfigureJsonSerialization(options, false);

        return options;
    });

    protected static JsonSerializerOptions JsonOptions => jsonOptions.Value;

    protected HttpClient ApiClient => webApiFixture.Client;
    protected IClock TestClock => webApiFixture.Clock;
    protected static string WebhookSecret => WebAppFactory.WebhookSecret;
    protected CancellationToken TestCancellationToken => TestContext.Current.CancellationToken;

    protected void AuthenticateAsTeacher(string studentId) => webApiFixture.AuthState.SetUser(studentId, ["teacher"]);
    protected void AuthenticateAsStudent(string studentId) => webApiFixture.AuthState.SetUser(studentId, ["student"]);
    /// <remarks>
    ///     Administrators are configured by username rather than carried in the token, so the caller has to be
    ///     the one <see cref="WebAppFactory.AdminStudentId" /> declares - the argument is accepted only so the
    ///     tests read the same way as the others.
    /// </remarks>
    protected void AuthenticateAsAdmin(string studentId)
    {
        studentId.Should().Be(WebAppFactory.AdminStudentId,
                              "administrators come from Keycloak:AdminUsers, not from the token");
        webApiFixture.AuthState.SetUser(studentId, ["teacher"]);
    }
    protected void Anonymous() => webApiFixture.AuthState.SetAnonymous();

    public async ValueTask InitializeAsync()
    {
        await webApiFixture.RestoreDatabaseAsync(ImportSeedDataAsync);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected virtual ValueTask ImportSeedDataAsync(DatabaseContext context) => ValueTask.CompletedTask;

    protected async ValueTask ModifyDatabaseContentAsync(Func<DatabaseContext, ValueTask> modifier)
    {
        await webApiFixture.ModifyDatabaseContentAsync(modifier);
    }
}
