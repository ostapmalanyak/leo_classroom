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

            courses.MapGet("/", GetCoursesAsync);
            courses.MapGet("/{id:long:min(1)}", GetCourseByIdAsync)
                   .WithName(nameof(GetCourseByIdAsync));
            courses.MapPost("/", AddCourseAsync);
            courses.MapPut("/{id:long:min(1)}", UpdateCourseAsync);
            courses.MapDelete("/{id:long:min(1)}", DeleteCourseAsync);
        }
    }

    private static async ValueTask<Ok<IReadOnlyCollection<CourseOverview>>> GetCoursesAsync(
        [FromServices] ICourseService service) =>
        TypedResults.Ok(await service.GetCourseOverviewsAsync());

    private static async ValueTask<Results<Ok<CourseDto>, NotFound>> GetCourseByIdAsync(
        [FromRoute] long id, [FromServices] ICourseService service)
    {
        var result = await service.GetCourseByIdAsync(id);

        return result.ToOk(CourseDto.FromCourse);
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
        var result = await service.AddCourseAsync(request.Title, request.RosterId);

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

public sealed record CourseCreateRequest(string Title, long RosterId)
{
    public sealed class Validator : AbstractValidator<CourseCreateRequest>
    {
        private const int MaxTitleLength = 200;

        public Validator()
        {
            RuleFor(c => c.Title).NotEmpty().MaximumLength(MaxTitleLength);
            RuleFor(c => c.RosterId).GreaterThan(0);
        }
    }
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
    Instant CreatedAt)
{
    public static CourseDto FromCourse(Course course) =>
        new(course.Id, course.Title, course.RosterId, course.OwnerId, course.IsReadOnly, course.StudentsRetainAccess,
            course.CreatedAt);
}
