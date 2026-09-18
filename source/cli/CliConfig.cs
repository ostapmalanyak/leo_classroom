namespace LeoClassroom.Cli;

// Runtime configuration. Non-secret endpoints come from environment variables (with localhost dev defaults);
// no client secret is ever embedded — the CLI uses a public Keycloak client with the device grant.
public sealed class CliConfig
{
    public required string BackendBaseUrl { get; init; }
    public required string ForgejoBaseUrl { get; init; }
    public required string KeycloakBaseUrl { get; init; }
    public required string Realm { get; init; }
    public required string ClientId { get; init; }

    // The backend API version this binary is built against (pinned; verified on startup).
    public const string ExpectedApiVersion = "1";

    public const string SchoolTimeZone = "Europe/Vienna";

    public string TokenEndpoint => $"{KeycloakBaseUrl.TrimEnd('/')}/realms/{Realm}/protocol/openid-connect/token";
    public string DeviceEndpoint =>
        $"{KeycloakBaseUrl.TrimEnd('/')}/realms/{Realm}/protocol/openid-connect/auth/device";

    public static CliConfig FromEnvironment()
    {
        return new CliConfig
        {
            BackendBaseUrl = Env("LEO_BACKEND_URL", "http://localhost:5080"),
            ForgejoBaseUrl = Env("LEO_FORGEJO_URL", "http://localhost:3000"),
            KeycloakBaseUrl = Env("LEO_KEYCLOAK_URL", "http://localhost:8080"),
            Realm = Env("LEO_REALM", "leo"),
            ClientId = Env("LEO_CLIENT_ID", "leo-classroom-cli")
        };
    }

    private static string Env(string name, string fallback)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
