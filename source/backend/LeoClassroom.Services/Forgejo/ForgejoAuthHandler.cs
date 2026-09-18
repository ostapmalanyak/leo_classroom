using System.Net.Http.Headers;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Options;

namespace LeoClassroom.Services.Forgejo;

internal sealed class ForgejoAuthHandler(IOptions<ForgejoSettings> settings) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                 CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("token", settings.Value.AdminToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
