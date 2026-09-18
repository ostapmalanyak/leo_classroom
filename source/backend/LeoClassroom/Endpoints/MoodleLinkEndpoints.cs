using FluentValidation;
using LeoClassroom.Auth;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services.Moodle;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints of a course's Moodle link
/// </summary>
public static class MoodleLinkEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapMoodleLinkEndpoints()
        {
            var moodleLink = app.MapGroup("/api/courses/{courseId:long:min(1)}/moodle-link")
                                .WithTags("Moodle")
                                .RequireAuthorization(AuthPolicies.RequireTeacher);

            moodleLink.MapGet("/", GetMoodleLinkAsync);
            moodleLink.MapPut("/", SetMoodleLinkAsync);
            moodleLink.MapDelete("/", DisableMoodleLinkAsync);
        }
    }

    private static async ValueTask<Results<Ok<MoodleLinkDto>, NotFound, ProblemHttpResult>> GetMoodleLinkAsync(
        [FromRoute] long courseId, [FromServices] IMoodleLinkService service)
    {
        var result = await service.GetAsync(courseId);

        return result.ToOk(MoodleLinkDto.FromView);
    }

    private static async ValueTask<Results<Ok<MoodleLinkDto>, ValidationProblem, NotFound, ProblemHttpResult>>
        SetMoodleLinkAsync([FromRoute] long courseId, [FromBody] MoodleLinkRequest request,
                           [FromServices] IMoodleLinkService service,
                           [FromServices] ITransactionProvider transaction)
    {
        if (new MoodleLinkRequest.Validator().Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        await transaction.BeginTransactionAsync();
        var result = await service.SetAsync(
            courseId, new MoodleLinkInput(request.Enabled, request.MoodleBaseUrl, request.MoodleCourseId,
                                          request.Token));

        return await result.CommitOkOrInvalidAsync(transaction, MoodleLinkDto.FromView);
    }

    private static async ValueTask<Results<NoContent, NotFound, ProblemHttpResult>> DisableMoodleLinkAsync(
        [FromRoute] long courseId, [FromServices] IMoodleLinkService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.DisableAsync(courseId);

        return await result.CommitNoContentAsync(transaction);
    }
}

public sealed record MoodleLinkRequest(bool Enabled, string MoodleBaseUrl, long MoodleCourseId, string? Token)
{
    public sealed class Validator : AbstractValidator<MoodleLinkRequest>
    {
        public Validator()
        {
            RuleFor(r => r.MoodleBaseUrl)
                .NotEmpty()
                .Must(BeAnAbsoluteHttpUrl)
                .When(r => r.Enabled)
                .WithMessage("A valid absolute http(s) Moodle base URL is required when the link is enabled.");
            RuleFor(r => r.MoodleCourseId).GreaterThan(0).When(r => r.Enabled);
        }

        // an absolute URL is not enough: the service calls this address server-side, so anything but http(s)
        // (file://, ftp://, ...) has to be rejected before it is stored
        private static bool BeAnAbsoluteHttpUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }
}

public sealed record MoodleLinkDto(bool Enabled, string MoodleBaseUrl, long MoodleCourseId, bool HasToken)
{
    public static MoodleLinkDto FromView(MoodleLinkView view) =>
        new(view.Enabled, view.MoodleBaseUrl, view.MoodleCourseId, view.HasToken);
}
