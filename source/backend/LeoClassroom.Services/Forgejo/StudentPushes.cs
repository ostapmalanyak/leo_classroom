using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;

namespace LeoClassroom.Services.Forgejo;

/// <summary>
///     Decides whether a push into a submission repository is the student's own
/// </summary>
/// <remarks>
///     Seeding a repository pushes as the bot, and a teacher may push into a student's repository too. Neither is
///     the student handing anything in, so they must not count as a submission, flag it late or show up in its
///     activity. The pusher is always a Forgejo account, so unlike a commit author it is never unknown.
/// </remarks>
internal static class StudentPushes
{
    public static async ValueTask<bool> IsByStudentAsync(IUnitOfWork uow, Acceptance acceptance,
                                                         WebhookEnvelope envelope)
    {
        if (envelope.Actor is null)
        {
            return false;
        }

        User? student = await uow.UserRepository.GetByIdAsync(acceptance.StudentId);

        return student is not null
               && string.Equals(student.StudentId, envelope.Actor, StringComparison.OrdinalIgnoreCase);
    }
}
