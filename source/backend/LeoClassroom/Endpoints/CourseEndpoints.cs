using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using LeoClassroom.Auth;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services;
using LeoClassroom.Shared;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ServiceNotFound = OneOf.Types.NotFound;
using ServiceSuccess = OneOf.Types.Success<LeoClassroom.Persistence.Model.Course>;
using CreateResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.CreatedAtRoute<LeoClassroom.Endpoints.CourseDto>,
    Microsoft.AspNetCore.Http.HttpResults.ValidationProblem,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.Conflict>;
using UpdateResult = Microsoft.AspNetCore.Http.HttpResults.Results<
    Microsoft.AspNetCore.Http.HttpResults.Ok<LeoClassroom.Endpoints.CourseDto>,
    Microsoft.AspNetCore.Http.HttpResults.ValidationProblem,
    Microsoft.AspNetCore.Http.HttpResults.NotFound,
    Microsoft.AspNetCore.Http.HttpResults.Conflict,
    Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints of the course resource
/// </summary>
public static class CourseEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapCourseEndpoints()
        {
            var courses = app.MapGroup("/api/courses")
                             .WithTags("Courses")
                             .RequireAuthorization(AuthPolicies.RequireTeacher);

            courses.MapGet("/avatar", GetAvatarAsync)
                   .AllowAnonymous();
            courses.MapGet("/", GetCoursesAsync);
            courses.MapGet("/{id:long:min(1)}", GetCourseByIdAsync)
                   .WithName(nameof(GetCourseByIdAsync));
            courses.MapPost("/", AddCourseAsync);
            courses.MapPut("/{id:long:min(1)}", UpdateCourseAsync);
            courses.MapDelete("/{id:long:min(1)}", DeleteCourseAsync);

            var courseParticipants = app.MapGroup("/api/courses")
                                        .WithTags("Courses")
                                        .RequireAuthorization();
            courseParticipants.MapGet("/{id:long:min(1)}/participants", GetParticipantsAsync);
        }
    }

    private static async ValueTask<Ok<IReadOnlyCollection<CourseOverview>>> GetCoursesAsync(
        [FromServices] ICourseService service) =>
        TypedResults.Ok(await service.GetCourseOverviewsAsync());

    private static IResult GetAvatarAsync([FromQuery] string? seed, HttpContext context)
    {
        if (seed is not { Length: >= 8 and <= 64 }
            || !seed.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
        {
            return TypedResults.BadRequest("Icon seeds must be 8 to 64 characters using letters, digits, or hyphens.");
        }

        context.Response.Headers.CacheControl = "public, max-age=31536000";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        int primaryHue = ((hash[0] << 8) | hash[1]) % 360;
        int accentHue = (primaryHue + 55 + hash[2] % 111) % 360;
        int secondaryHue = (primaryHue + 180 + hash[3] % 61) % 360;
        var svg = new StringBuilder("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 320">
            """);
        svg.AppendLine($"""<rect width="320" height="320" fill="hsl({primaryHue} 42% 14%)"/>""");

        switch (hash[4] % 3)
        {
            case 0:
                svg.AppendLine($"""<circle cx="160" cy="160" r="152" fill="hsl({secondaryHue} 65% 48%)" opacity=".22"/>""");
                break;
            case 1:
                svg.AppendLine($"""<path d="M160 8 312 160 160 312 8 160Z" fill="hsl({secondaryHue} 65% 48%)" opacity=".22"/>""");
                break;
            default:
                svg.AppendLine($"""<path d="M0 64h320v24H0zm0 112h320v24H0zm0 112h320v24H0Z" fill="hsl({secondaryHue} 65% 48%)" opacity=".2"/>""");
                break;
        }

        const int cellSize = 44;
        const int cellGap = 12;
        const int gridOffset = 26;
        for (int row = 0; row < 5; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                int bitIndex = row * 3 + column;
                bool filled = (hash[5 + bitIndex / 8] & (1 << (bitIndex % 8))) != 0;
                if (!filled)
                {
                    continue;
                }

                int y = gridOffset + row * (cellSize + cellGap);
                string color = (hash[8 + bitIndex] & 1) == 0
                    ? $"hsl({accentHue} 82% 68%)"
                    : $"hsl({secondaryHue} 76% 74%)";
                foreach (int mirroredColumn in column == 2 ? new[] { column } : new[] { column, 4 - column })
                {
                    int x = gridOffset + mirroredColumn * (cellSize + cellGap);
                    svg.AppendLine($"""<rect x="{x}" y="{y}" width="{cellSize}" height="{cellSize}" rx="8" fill="{color}"/>""");
                }
            }
        }

        svg.AppendLine("</svg>");

        return TypedResults.Content(svg.ToString(), "image/svg+xml; charset=utf-8");
    }

    private static bool IsValidIconSeed(string? seed) =>
        seed is { Length: >= 8 and <= 64 }
        && seed.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static async ValueTask<Results<Ok<CourseDto>, NotFound>> GetCourseByIdAsync(
        [FromRoute] long id, [FromServices] ICourseService service)
    {
        var result = await service.GetCourseByIdAsync(id);

        return result.ToOk(CourseDto.FromCourse);
    }

    private static async ValueTask<Results<Ok<CourseParticipantsDto>, NotFound, ProblemHttpResult>>
        GetParticipantsAsync([FromRoute] long id, [FromServices] ICourseService service)
    {
        var result = await service.GetParticipantsAsync(id);

        return result.Match<Results<Ok<CourseParticipantsDto>, NotFound, ProblemHttpResult>>(
            participants => TypedResults.Ok(CourseParticipantsDto.FromParticipants(participants)),
            _ => TypedResults.NotFound(),
            _ => ApiResults.Forbidden());
    }

    private static async ValueTask<CreateResult> AddCourseAsync(
        [FromBody] CourseCreateRequest request, [FromServices] ICourseService service,
        [FromServices] ITransactionProvider transaction)
    {
        if (new CourseCreateRequest.Validator().Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        await transaction.BeginTransactionAsync();
        var result = await service.AddCourseAsync(request.EffectiveTitle, request.EffectiveRosterId, request.IconSeed);

        // the branches are local functions rather than lambdas, so that Match stays readable
        return await result.Match(OnCreatedAsync, OnNotFoundAsync, OnConflictAsync);

        async ValueTask<CreateResult> OnCreatedAsync(ServiceSuccess success)
        {
            // committing is explicit in the success case, rolling back is the default
            await transaction.CommitAsync();

            return TypedResults.CreatedAtRoute(CourseDto.FromCourse(success.Value), nameof(GetCourseByIdAsync),
                                               new { id = success.Value.Id });
        }

        // left uncommitted on purpose - scoped unit-of-work disposal rolls the transaction back
        static ValueTask<CreateResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<CreateResult>(TypedResults.NotFound());

        static ValueTask<CreateResult> OnConflictAsync(ICourseService.AlreadyExists alreadyExists) =>
            ValueTask.FromResult<CreateResult>(TypedResults.Conflict());
    }

    private static async ValueTask<UpdateResult> UpdateCourseAsync(
        [FromRoute] long id, [FromBody] CourseUpdateRequest request, [FromServices] ICourseService service,
        [FromServices] ITransactionProvider transaction)
    {
        if (new CourseUpdateRequest.Validator().Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        await transaction.BeginTransactionAsync();
        var result =
            await service.UpdateCourseAsync(id, request.Title, request.IsReadOnly, request.StudentsRetainAccess);

        return await result.Match(OnUpdatedAsync, OnNotFoundAsync, OnConflictAsync, OnForbiddenAsync);

        async ValueTask<UpdateResult> OnUpdatedAsync(ServiceSuccess success)
        {
            await transaction.CommitAsync();

            return TypedResults.Ok(CourseDto.FromCourse(success.Value));
        }

        static ValueTask<UpdateResult> OnNotFoundAsync(ServiceNotFound notFound) =>
            ValueTask.FromResult<UpdateResult>(TypedResults.NotFound());

        static ValueTask<UpdateResult> OnConflictAsync(ICourseService.AlreadyExists alreadyExists) =>
            ValueTask.FromResult<UpdateResult>(TypedResults.Conflict());

        static ValueTask<UpdateResult> OnForbiddenAsync(Forbidden forbidden) =>
            ValueTask.FromResult<UpdateResult>(ApiResults.Forbidden());
    }

    private static async ValueTask<Results<NoContent, NotFound, ProblemHttpResult>>
        DeleteCourseAsync([FromRoute] long id, [FromServices] ICourseService service,
                          [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.DeleteCourseAsync(id);

        return await result.CommitNoContentAsync(transaction);
    }
}

public sealed record CourseCreateRequest(
    string? Title = null, long? RosterId = null, string? Name = null, string[]? RosterIds = null,
    string? IconSeed = null)
{
    public sealed class Validator : AbstractValidator<CourseCreateRequest>
    {
        public Validator()
        {
            RuleFor(c => c.Name)
                .NotEmpty()
                .Must(name => name is not null && name == name.Trim())
                .MaximumLength(50)
                .When(c => c.Name is not null || c.RosterIds is not null);
            RuleFor(c => c.RosterIds)
                .NotNull()
                .Must(ids => ids is { Length: 1 } && long.TryParse(
                    ids[0], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long rosterId) && rosterId > 0)
                .When(c => c.Name is not null || c.RosterIds is not null)
                .WithMessage("Select exactly one valid roster.");
            RuleFor(c => c.Title)
                .NotEmpty()
                .MaximumLength(200)
                .When(c => c.Name is null && c.RosterIds is null);
            RuleFor(c => c.RosterId)
                .NotNull()
                .GreaterThan(0)
                .When(c => c.Name is null && c.RosterIds is null);
            RuleFor(c => c.IconSeed)
                .Must(IsValidIconSeed)
                .When(c => c.IconSeed is not null)
                .WithMessage("Icon seeds must be 8 to 64 characters using letters, digits, or hyphens.");
        }

        private static bool IsValidIconSeed(string? seed) =>
            seed is { Length: >= 8 and <= 64 }
            && seed.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');
    }

    public string EffectiveTitle => Name?.Trim() ?? Title!.Trim();

    public long EffectiveRosterId => RosterIds is { Length: 1 }
        ? long.Parse(RosterIds[0], System.Globalization.CultureInfo.InvariantCulture)
        : RosterId!.Value;
}

public sealed record CourseUpdateRequest(string Title, bool IsReadOnly, bool StudentsRetainAccess)
{
    public sealed class Validator : AbstractValidator<CourseUpdateRequest>
    {
        private const int MaxTitleLength = 200;

        public Validator()
        {
            RuleFor(c => c.Title).NotEmpty().MaximumLength(MaxTitleLength);
        }
    }
}

public sealed record CourseDto(
    long Id,
    string Title,
    long RosterId,
    long OwnerId,
    bool IsReadOnly,
    bool StudentsRetainAccess,
    Instant CreatedAt,
    string? IconSeed)
{
    public static CourseDto FromCourse(Course course) =>
        new(course.Id, course.Title, course.RosterId, course.OwnerId, course.IsReadOnly, course.StudentsRetainAccess,
            course.CreatedAt, course.IconSeed);
}

public sealed record CourseParticipantDto(string FirstName, string LastName, string? Class, Role Role);

public sealed record CourseParticipantsDto(string Title, IReadOnlyCollection<CourseParticipantDto> Participants)
{
    public static CourseParticipantsDto FromParticipants(CourseParticipants participants) =>
        new(participants.Title, participants.Participants
            .Select(participant =>
                new CourseParticipantDto(participant.FirstName, participant.LastName, participant.Class,
                                         participant.Role))
            .ToArray());
}
