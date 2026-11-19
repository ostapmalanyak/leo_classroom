using System.Net;
using System.Text;

namespace LeoClassroom.TestInt.Util;

/// <summary>
///     Stands in for Forgejo, which is never reachable in a test. It answers only the read-only lookups the
///     endpoints under test depend on - every user has an account, every repository has no commits yet - and
///     every other call fails the same way the unreachable test host does.
/// </summary>
internal sealed class FakeForgejoHandler : HttpMessageHandler
{
    private const string UsersPrefix = "/api/v1/users/";
    private const string ReposPrefix = "/api/v1/repos/";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                           CancellationToken cancellationToken)
    {
        string path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (request.Method == HttpMethod.Get && path.StartsWith(UsersPrefix, StringComparison.Ordinal))
        {
            string login = Uri.UnescapeDataString(path[UsersPrefix.Length..]);
            if (!login.Contains('/', StringComparison.Ordinal))
            {
                return Json($$"""{"id":1,"login":"{{login}}"}""");
            }
        }
        if (request.Method == HttpMethod.Get && path.StartsWith(ReposPrefix, StringComparison.Ordinal)
                                             && path.EndsWith("/commits", StringComparison.Ordinal))
        {
            return Json("[]");
        }

        throw new HttpRequestException($"Forgejo is not available in tests ({request.Method} {path})");
    }

    private static Task<HttpResponseMessage> Json(string body) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
}
