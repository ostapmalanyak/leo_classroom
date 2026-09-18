using LeoClassroom.Cli;

CliConfig config = CliConfig.FromEnvironment();
using var http = new HttpClient();
var backend = new BackendClient(http, config);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("LEO Classroom — publish assignment");

// Pin to a backend API version; refuse to run against an incompatible backend before any side effect.
string? apiVersion = await backend.GetApiVersionAsync(cts.Token);
if (apiVersion is null)
{
    Console.Error.WriteLine($"Could not reach the backend at {config.BackendBaseUrl}.");

    return 2;
}
if (apiVersion != CliConfig.ExpectedApiVersion)
{
    Console.Error.WriteLine(
        $"This tool targets API v{CliConfig.ExpectedApiVersion} but the backend is v{apiVersion}. " +
        "Download a matching tool version from the teacher area.");

    return 2;
}

var auth = new DeviceAuth(http, config, new TokenCache(TokenCache.DefaultPath()));
AuthResult login = await auth.AuthenticateAsync(cts.Token);
if (!login.Ok)
{
    Console.Error.WriteLine(login.Message);

    return 3;
}
if (string.IsNullOrWhiteSpace(login.Username))
{
    Console.Error.WriteLine("Could not determine your username from the token.");

    return 3;
}

backend.UseToken(login.AccessToken!);
var flow = new PublishFlow(config, backend, login.Username);

return await flow.RunAsync(Directory.GetCurrentDirectory(), cts.Token);
