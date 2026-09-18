using LeoClassroom.Services.Auth;
using LeoClassroom.Services.Security;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Moodle;

public sealed record MoodleLinkInput(bool Enabled, string MoodleBaseUrl, long MoodleCourseId, string? Token);

public interface IMoodleLinkService
{
    public ValueTask<OneOf<MoodleLinkView, NotFound, Forbidden>> GetAsync(long courseId);
    public ValueTask<OneOf<MoodleLinkView, NotFound, Forbidden>> SetAsync(long courseId, MoodleLinkInput input);
    public ValueTask<OneOf<Success, NotFound, Forbidden>> DisableAsync(long courseId);
}

internal sealed class MoodleLinkService(
    IUnitOfWork uow,
    ICurrentUser currentUser,
    ISecretProtector protector) : IMoodleLinkService
{
    public ValueTask<OneOf<MoodleLinkView, NotFound, Forbidden>> GetAsync(long courseId) =>
        AsCourseTeacherAsync<MoodleLinkView>(courseId, async () =>
        {
            MoodleLink? link = await uow.MoodleLinkRepository.GetByCourseIdAsync(courseId);

            return ToView(link);
        });

    public ValueTask<OneOf<MoodleLinkView, NotFound, Forbidden>> SetAsync(long courseId, MoodleLinkInput input) =>
        AsCourseTeacherAsync<MoodleLinkView>(courseId, async () =>
        {
            MoodleLink? link = await uow.MoodleLinkRepository.GetTrackedByCourseIdAsync(courseId);
            if (link is null)
            {
                link = new MoodleLink { CourseId = courseId, MoodleBaseUrl = input.MoodleBaseUrl };
                uow.MoodleLinkRepository.Add(link);
            }

            link.Enabled = input.Enabled;
            link.MoodleBaseUrl = input.MoodleBaseUrl;
            link.MoodleCourseId = input.MoodleCourseId;
            if (!string.IsNullOrWhiteSpace(input.Token))
            {
                link.TokenCipher = protector.Protect(input.Token);
            }
            await uow.SaveChangesAsync();

            return ToView(link);
        });

    public ValueTask<OneOf<Success, NotFound, Forbidden>> DisableAsync(long courseId) =>
        AsCourseTeacherAsync<Success>(courseId, async () =>
        {
            MoodleLink? link = await uow.MoodleLinkRepository.GetTrackedByCourseIdAsync(courseId);
            if (link is not null && link.Enabled)
            {
                link.Enabled = false;
                await uow.SaveChangesAsync();
            }

            return new Success();
        });

    /// <summary>
    ///     Runs <paramref name="work" /> when the caller teaches the course, and otherwise answers the refusal
    ///     <see cref="AuthorizeAsync" /> produced
    /// </summary>
    /// <remarks>
    ///     Every operation on a Moodle link is gated the same way, so the match over the three cases is written
    ///     here once rather than at the head of each of them.
    /// </remarks>
    private async ValueTask<OneOf<TValue, NotFound, Forbidden>> AsCourseTeacherAsync<TValue>(
        long courseId, Func<ValueTask<TValue>> work)
    {
        OneOf<Course, NotFound, Forbidden> course = await AuthorizeAsync(courseId);

        return await course.Match<ValueTask<OneOf<TValue, NotFound, Forbidden>>>(
            async authorized => await work(),
            notFound => ValueTask.FromResult<OneOf<TValue, NotFound, Forbidden>>(notFound),
            forbidden => ValueTask.FromResult<OneOf<TValue, NotFound, Forbidden>>(forbidden));
    }

    private async ValueTask<OneOf<Course, NotFound, Forbidden>> AuthorizeAsync(long courseId)
    {
        Course? course = await uow.CourseRepository.GetWithTeachersAsync(courseId);
        if (course is null)
        {
            return new NotFound();
        }

        return IsCourseTeacher(course) ? course : new Forbidden();
    }

    private bool IsCourseTeacher(Course course) =>
        currentUser.Roles.Contains(Role.Admin)
        || string.Equals(course.Owner.StudentId, currentUser.StudentId, StringComparison.Ordinal)
        || course.CoTeachers.Any(t => string.Equals(t.StudentId, currentUser.StudentId, StringComparison.Ordinal));

    private static MoodleLinkView ToView(MoodleLink? link) =>
        link is null
            ? new MoodleLinkView(false, string.Empty, 0, false)
            : new MoodleLinkView(link.Enabled, link.MoodleBaseUrl, link.MoodleCourseId, link.TokenCipher is not null);
}
