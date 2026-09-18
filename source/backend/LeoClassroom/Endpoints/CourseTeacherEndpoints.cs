using FluentValidation;
using LeoClassroom.Auth;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Services;
using LeoClassroom.Util;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace LeoClassroom.Endpoints;

/// <summary>
///     The endpoints of the teachers assigned to a course
/// </summary>
public static class CourseTeacherEndpoints
{
    extension(IEndpointRouteBuilder app)
    {
        public void MapCourseTeacherEndpoints()
        {
            var teachers = app.MapGroup("/api/courses/{courseId:long:min(1)}/teachers")
                              .WithTags("Courses")
                              .RequireAuthorization(AuthPolicies.RequireTeacher);

            teachers.MapGet("/", ListTeachersAsync);
            teachers.MapPost("/", AddTeacherAsync);
            teachers.MapDelete("/{userId:long:min(1)}", RemoveTeacherAsync);
        }
    }

    private static async ValueTask<Results<Ok<CourseTeachers>, NotFound>> ListTeachersAsync(
        [FromRoute] long courseId, [FromServices] ICourseTeacherService service)
    {
        var result = await service.ListAsync(courseId);

        return result.ToOk();
    }

    private static async ValueTask<Results<NoContent, ValidationProblem, NotFound, ProblemHttpResult>>
        AddTeacherAsync([FromRoute] long courseId, [FromBody] CourseTeacherRequest request,
                        [FromServices] ICourseTeacherService service,
                        [FromServices] ITransactionProvider transaction)
    {
        if (new CourseTeacherRequest.Validator().Check(request) is { } validationProblem)
        {
            return validationProblem;
        }

        await transaction.BeginTransactionAsync();
        var result = await service.AddAsync(courseId, request.UserId);

        return await result.CommitNoContentOrInvalidAsync(transaction);
    }

    private static async ValueTask<Results<NoContent, NotFound, ProblemHttpResult>> RemoveTeacherAsync(
        [FromRoute] long courseId, [FromRoute] long userId, [FromServices] ICourseTeacherService service,
        [FromServices] ITransactionProvider transaction)
    {
        await transaction.BeginTransactionAsync();
        var result = await service.RemoveAsync(courseId, userId);

        return await result.CommitNoContentAsync(transaction);
    }
}

public sealed record CourseTeacherRequest(long UserId)
{
    public sealed class Validator : AbstractValidator<CourseTeacherRequest>
    {
        public Validator() => RuleFor(r => r.UserId).GreaterThan(0);
    }
}
