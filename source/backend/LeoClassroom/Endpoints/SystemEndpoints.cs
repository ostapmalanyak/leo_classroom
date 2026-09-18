using Microsoft.AspNetCore.Http.HttpResults;

namespace LeoClassroom.Endpoints;

/// <summary>
///     Endpoints that describe the service itself rather than a resource
/// </summary>
public static class SystemEndpoints
{
    /// <summary>
    ///     The API version the teacher CLI pins against; bump on a breaking contract change
    /// </summary>
    public const string ApiVersion = "1";

    extension(IEndpointRouteBuilder app)
    {
        public void MapSystemEndpoints()
        {
            var system = app.MapGroup(string.Empty)
                            .WithTags("System")
                            .AllowAnonymous();

            system.MapGet("/healthz", GetHealth);
            system.MapGet("/api/version", GetVersion);
        }
    }

    private static Ok<string> GetHealth() => TypedResults.Ok("ok");

    private static Ok<ApiVersionDto> GetVersion() => TypedResults.Ok(new ApiVersionDto(ApiVersion));
}

public sealed record ApiVersionDto(string ApiVersion);
