using LeoClassroom.Services.Forgejo;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoint Forgejo posts its repository events to
/// </summary>
/// <remarks>
///     This is the only anonymous write endpoint of the API. It is not authenticated by Keycloak but by the HMAC
///     signature Forgejo computes over the raw body with the shared webhook secret, so the body has to be read as
///     bytes and verified before anything else looks at it.
/// </remarks>
public static class WebhookEndpoints
{
    /// <summary>
    ///     Upper bound for a webhook body. Forgejo push payloads are a few kilobytes; this only exists so that an
    ///     unauthenticated caller cannot make the server buffer an arbitrary amount of memory before the signature
    ///     is even checked.
    /// </summary>
    private const int MaxBodyBytes = 1024 * 1024;

    extension(IEndpointRouteBuilder app)
    {
        public void MapWebhookEndpoints()
        {
            var webhooks = app.MapGroup("/api/webhooks/forgejo")
                              .WithTags("Webhooks")
                              .AllowAnonymous();

            webhooks.MapPost("/", ReceiveForgejoWebhookAsync);
        }
    }

    private static async ValueTask<Results<NoContent, UnauthorizedHttpResult, StatusCodeHttpResult>>
        ReceiveForgejoWebhookAsync(HttpRequest request, [FromServices] IWebhookService webhooks,
                                   [FromServices] ILoggerFactory loggerFactory,
                                   CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(WebhookEndpoints));

        if (request.ContentLength > MaxBodyBytes)
        {
            logger.LogWarning("Rejected Forgejo webhook with a {Length} byte body", request.ContentLength);

            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        byte[]? body = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            logger.LogWarning("Rejected Forgejo webhook whose body exceeded {MaxBodyBytes} bytes", MaxBodyBytes);

            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string? signature = FirstHeader(request, "X-Forgejo-Signature", "X-Gitea-Signature");
        if (!webhooks.VerifySignature(body, signature))
        {
            logger.LogWarning("Rejected Forgejo webhook with missing or invalid signature");

            return TypedResults.Unauthorized();
        }

        string eventType = FirstHeader(request, "X-Forgejo-Event", "X-Gitea-Event") ?? "unknown";
        await webhooks.AcceptAsync(eventType, body);

        return TypedResults.NoContent();
    }

    /// <summary>
    ///     Reads the raw request body, or null when it turns out to be larger than <see cref="MaxBodyBytes" />
    /// </summary>
    /// <remarks>
    ///     <c>Content-Length</c> is checked separately but cannot be trusted on its own - it is absent for a chunked
    ///     request - so the copy itself is bounded as well.
    /// </remarks>
    private static async ValueTask<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8 * 1024];

        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static string? FirstHeader(HttpRequest request, params string[] names)
    {
        foreach (string name in names)
        {
            if (request.Headers.TryGetValue(name, out Microsoft.Extensions.Primitives.StringValues value))
            {
                return value.ToString();
            }
        }

        return null;
    }
}
