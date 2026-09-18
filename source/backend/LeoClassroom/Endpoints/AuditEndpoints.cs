using LeoClassroom.Auth;
using LeoClassroom.Services.Audit;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints of the audit log
/// </summary>
public static class AuditEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapAuditEndpoints()
        {
            var audit = app.MapGroup("/api/audit")
                           .WithTags("Audit")
                           .RequireAuthorization(AuthPolicies.RequireAdmin);

            audit.MapGet("/", QueryAuditEventsAsync);
        }
    }

    private static async ValueTask<Results<Ok<IReadOnlyCollection<AuditEventView>>, ValidationProblem>>
        QueryAuditEventsAsync([FromQuery] IsoInstant? from, [FromQuery] IsoInstant? to, [FromQuery] string? actor,
                              [FromQuery] AuditAction? action, [FromQuery] string? targetType,
                              [FromQuery] string? targetId, [FromServices] IAuditQueryService service,
                              [FromQuery] int skip = 0, [FromQuery] int take = DefaultPageSize)
    {
        if (new AuditQuery(skip, take).Validate() is { } validationProblem)
        {
            return validationProblem;
        }

        var filter = new AuditFilter(from?.Value, to?.Value, actor, action, targetType, targetId, skip, take);

        return TypedResults.Ok(await service.QueryAsync(filter));
    }

    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 500;

    /// <summary>
    ///     Bounds the paging window, so that a caller cannot ask the audit log for an unbounded page
    /// </summary>
    private readonly record struct AuditQuery(int Skip, int Take)
    {
        public ValidationProblem? Validate()
        {
            if (Skip < 0)
            {
                return TypedResults.ValidationProblem(Validation.Failure(nameof(Skip), "Must not be negative"));
            }

            return Take is < 1 or > MaxPageSize
                ? TypedResults.ValidationProblem(
                    Validation.Failure(nameof(Take), $"Must be between 1 and {MaxPageSize}"))
                : null;
        }
    }
}
