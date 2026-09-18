using System.Net;
using System.Security.Claims;
using LeoClassroom.Auth;
using LeoClassroom.Cli;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Shared;

// No network, external packages, or deletion. Fixtures stay beneath this project's ignored obj directory.
string fixtures = Path.GetFullPath(Path.Combine("obj", "fixtures", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(fixtures);
int checks = 0;

void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
    checks++;
}

(string Dn, bool Teacher)[] distinguishedNames =
[
    ("CN=IF001,OU=Teachers,DC=school", true),
    ("CN=Last\\, First, ou = teachers ,DC=school", true),
    ("CN=IF001+OU=Teachers,DC=school", true),
    ("CN=IF001,OU=TeachersAlumni,DC=school", false),
    ("CN=OU=Teachers,OU=Students,DC=school", false),
    ("CN=Name\\,OU=Teachers,OU=Students,DC=school", false),
    ("CN=Name\\+OU=Teachers,OU=Students,DC=school", false),
    ("CN=\"Name,OU=Teachers\",OU=Students,DC=school", false),
    ("CN=Name\\", false),
    ("", false)
];
foreach ((string dn, bool teacher) in distinguishedNames)
{
    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim("preferred_username", "IF001"), new Claim("ldap_entry_dn", dn)], "test"));
    var roles = AuthClaims.ReadRoles(principal, new HashSet<string>());
    Check(roles.Contains(Role.Teacher) == teacher, $"Incorrect teacher role for {dn}");
}
var admin = new ClaimsPrincipal(new ClaimsIdentity([new Claim("preferred_username", "admin")], "test"));
Check(AuthClaims.ReadRoles(admin, new HashSet<string> { "admin" }).Contains(Role.Admin), "Configured admin lost role");
var incoming = new ClaimsPrincipal(new ClaimsIdentity(
    [new Claim("preferred_username", "IF001"), new Claim("ldap_entry_dn", "CN=IF001,OU=Students,DC=school"),
     new Claim(ClaimTypes.Role, "admin")], "test"));
var transformation = new LeoRoleClaimsTransformation(new HashSet<string> { "admin" });
ClaimsPrincipal transformed = await transformation.TransformAsync(incoming);
Check(!transformed.IsInRole("admin") && transformed.IsInRole("student"), "Incoming role bypassed LDAP derivation");
Check(incoming.IsInRole("admin"), "Transformation mutated the authentication principal");
ClaimsPrincipal transformedAgain = await transformation.TransformAsync(transformed);
Check(transformedAgain.FindAll(ClaimTypes.Role).Count() == 1, "Repeated transformation duplicated roles");
Check((await transformation.TransformAsync(admin)).IsInRole("admin"), "Configured admin lost policy access");

string ignorePath = Path.Combine(fixtures, ".gitignore");
const string originalIgnore = "# Keep secrets private\r\n.env\r\nsecrets/\r\nnode_modules/";
File.WriteAllText(ignorePath, originalIgnore);
var excludes = GitPreparer.ComputeExcludes("custom-cli");
GitPreparer.WriteExcludes(fixtures, excludes);
string combined = File.ReadAllText(ignorePath);
Check(combined.StartsWith(originalIgnore + "\n", StringComparison.Ordinal), "Existing ignore rules changed");
Check(combined.Contains("\ncustom-cli\n", StringComparison.Ordinal), "CLI exclusion missing");
GitPreparer.WriteExcludes(fixtures, excludes);
Check(File.ReadAllText(ignorePath) == combined, "Repeated exclusions must be idempotent");
string emptyRepo = Path.Combine(fixtures, "empty");
Directory.CreateDirectory(emptyRepo);
GitPreparer.WriteExcludes(emptyRepo, excludes);
Check(File.ReadAllLines(Path.Combine(emptyRepo, ".gitignore")).Contains(".leo/"), "New ignore file missing rules");

string cachePath = Path.Combine(fixtures, "tokens", "token.json");
var cache = new TokenCache(cachePath);
Check(cache.Read() is null, "Missing token cache should be empty");
var tokens = new CachedTokens("access-token-without-roles", "refresh", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds());
cache.Write(tokens);
Check(cache.Read() == tokens, "Token cache round trip failed");
if (!OperatingSystem.IsWindows())
{
    var ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    Check(File.GetUnixFileMode(cachePath) == ownerOnly, "New token cache is not private");
    File.SetUnixFileMode(cachePath, ownerOnly | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    cache.Write(tokens with { RefreshToken = "r" });
    Check(File.GetUnixFileMode(cachePath) == ownerOnly, "Existing cache permissions were not repaired");
    Check(cache.Read()?.RefreshToken == "r", "Cache replacement left trailing bytes");
}

foreach (string path in new[] { "../secret", "/secret", ".git/config", "src/.GIT/config", "C:\\secret", "\\secret", ".", "" })
{
    Check(!RepositoryFilePath.IsSafe(path), $"Unsafe repository path accepted: {path}");
}
Check(RepositoryFilePath.IsSafe("docs/README.md"), "Ordinary path rejected");
Check(!RepositoryFilePath.ContainsLink(fixtures, "missing/README.md"), "New file considered a symlink");
File.WriteAllText(Path.Combine(fixtures, "README.md"), "normal");
Check(!RepositoryFilePath.ContainsLink(fixtures, "README.md"), "Regular file considered a symlink");
if (!OperatingSystem.IsWindows())
{
    string protectedFile = Path.Combine(fixtures, "protected.txt");
    File.WriteAllText(protectedFile, "unchanged");
    File.CreateSymbolicLink(Path.Combine(fixtures, "linked-readme"), protectedFile);
    Directory.CreateSymbolicLink(Path.Combine(fixtures, "linked-dir"), emptyRepo);
    File.CreateSymbolicLink(Path.Combine(fixtures, "broken-link"), Path.Combine(fixtures, "absent"));
    Check(RepositoryFilePath.ContainsLink(fixtures, "linked-readme"), "File symlink accepted");
    Check(RepositoryFilePath.ContainsLink(fixtures, "linked-dir/new.md"), "Parent directory symlink accepted");
    Check(RepositoryFilePath.ContainsLink(fixtures, "broken-link"), "Broken symlink accepted");
    Check(File.ReadAllText(protectedFile) == "unchanged", "Symlink check changed the target");
}

var config = new CliConfig
{
    BackendBaseUrl = "https://backend.invalid",
    ForgejoBaseUrl = "https://git.invalid",
    KeycloakBaseUrl = "https://login.invalid",
    Realm = "school",
    ClientId = "cli"
};
foreach (string role in new[] { "Teacher", "Admin", "Student" })
{
    using var handler = new StubHttpHandler(request =>
    {
        Check(request.RequestUri?.AbsoluteUri == "https://backend.invalid/api/me", "Gate called the wrong endpoint");
        Check(request.Headers.Authorization?.Parameter == tokens.AccessToken, "Gate did not authenticate the request");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"studentId":"IF001","roles":["{{role}}"]}""")
        };
    });
    using var http = new HttpClient(handler);
    AuthResult result = await new DeviceAuth(http, config, cache).AuthenticateAsync(CancellationToken.None);
    Check(result.Ok == (role is "Teacher" or "Admin"), $"Wrong CLI access for {role}");
    Check(!result.Ok || result.Username == "IF001", "CLI did not use backend identity");
    Check(http.DefaultRequestHeaders.Authorization is null, "Gate leaked bearer header into OAuth requests");
}
using (var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)))
using (var http = new HttpClient(handler))
{
    AuthResult result = await new DeviceAuth(http, config, cache).AuthenticateAsync(CancellationToken.None);
    Check(!result.Ok && result.AccessToken is null, "Retired or forbidden account was accepted");
}
foreach (string body in new[] { "<html>upstream failed</html>", "null", "{}", "{\"studentId\":\"IF001\",\"roles\":null}" })
{
    using var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(body)
    });
    using var http = new HttpClient(handler);
    AuthResult result = await new DeviceAuth(http, config, cache).AuthenticateAsync(CancellationToken.None);
    Check(!result.Ok, "Malformed account response granted CLI access");
}

Console.WriteLine($"Passed {checks} offline regression checks.");

internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(respond(request));
    }
}
