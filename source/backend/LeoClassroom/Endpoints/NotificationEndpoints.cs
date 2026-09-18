using LeoClassroom.Auth;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Notifications;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ServiceNotFound = OneOf.Types.NotFound;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints of the caller's own notification preferences
/// </summary>
public static class NotificationEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapNotificationEndpoints()
        {
            var notifications = app.MapGroup("/api/notifications")
                                   .WithTags("Notifications")
                                   .RequireAuthorization(AuthPolicies.RequireStudent);

            notifications.MapGet("/preferences", GetPreferencesAsync);
            notifications.MapPut("/preferences", UpdatePreferencesAsync);
        }
    }

    private static async ValueTask<Results<Ok<NotificationPreferenceDto>, NotFound>> GetPreferencesAsync(
        [FromServices] INotificationPreferenceService service)
    {
        var result = await service.GetAsync();

        return result.ToOk(NotificationPreferenceDto.FromView);
    }

    private static async ValueTask<Results<Ok<NotificationPreferenceDto>, NotFound>> UpdatePreferencesAsync(
        [FromBody] NotificationPreferenceDto request, [FromServices] INotificationPreferenceService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.UpdateAsync(request.NewAssignment, request.DeadlineChanged);

        return await result.Match(OnUpdatedAsync, OnNotFoundAsync);

        async ValueTask<Results<Ok<NotificationPreferenceDto>, NotFound>> OnUpdatedAsync(
            NotificationPreferenceView view)
        {
            await transaction.CommitAsync();

            return TypedResults.Ok(NotificationPreferenceDto.FromView(view));
        }

        // left uncommitted on purpose - scoped unit-of-work disposal rolls the transaction back
        static ValueTask<Results<Ok<NotificationPreferenceDto>, NotFound>> OnNotFoundAsync(
            ServiceNotFound notFound) =>
            ValueTask.FromResult<Results<Ok<NotificationPreferenceDto>, NotFound>>(TypedResults.NotFound());
    }
}

public sealed record NotificationPreferenceDto(bool NewAssignment, bool DeadlineChanged)
{
    public static NotificationPreferenceDto FromView(NotificationPreferenceView view) =>
        new(view.NewAssignment, view.DeadlineChanged);
}
