using System.Net;

namespace LeoClassroom.Services.Forgejo;

/// <summary>
///     Turns a Forgejo that cannot be reached into a 502 response instead of an exception
/// </summary>
/// <remarks>
///     Every <see cref="ForgejoClient" /> method already maps a failed response to a <see cref="ForgejoError" />,
///     which the endpoints answer with a 502. A refused connection, an unknown host or a dropped socket would
///     otherwise bypass that and surface as an unhandled 500. The exception is logged here; the synthesized
///     response carries only a generic reason, so no host name or socket detail reaches a client.
/// </remarks>
internal sealed class ForgejoTransportHandler(ILogger<ForgejoTransportHandler> logger) : DelegatingHandler
{
    public const string UnreachableReason = "Forgejo is unreachable";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                 CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Forgejo request {Method} {Path} could not be sent", request.Method,
                            request.RequestUri?.AbsolutePath);

            return new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                ReasonPhrase = UnreachableReason,
                RequestMessage = request
            };
        }
    }
}
