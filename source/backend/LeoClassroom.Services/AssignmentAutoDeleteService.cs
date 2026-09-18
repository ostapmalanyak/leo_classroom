using LeoClassroom.Services.Audit;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services;

public interface IAssignmentAutoDeleteService
{
    public ValueTask<int> RunAsync();
}

internal sealed class AssignmentAutoDeleteService(
    IUnitOfWork uow,
    IDeletionCascade deletion,
    IAuditLog audit,
    IClock clock,
    ILogger<AssignmentAutoDeleteService> logger) : IAssignmentAutoDeleteService
{
    public async ValueTask<int> RunAsync()
    {
        IReadOnlyCollection<long> due =
            await uow.AssignmentRepository.GetDueForAutoDeleteAsync(clock.GetCurrentInstant());

        int deleted = 0;
        foreach (long id in due)
        {
            try
            {
                if (await DeleteOneAsync(id))
                {
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto-delete of assignment {Id} failed", id);
            }
        }

        return deleted;
    }

    private async ValueTask<bool> DeleteOneAsync(long id)
    {
        Assignment? assignment = await uow.AssignmentRepository.GetWithAcceptancesAsync(id);
        if (assignment is null)
        {
            return false;
        }

        CascadeResult cascade = await deletion.DeleteAssignmentsAsync([id]);
        await uow.SaveChangesAsync();
        await audit.RecordAsync(AuditAction.AssignmentAutoDeleted, "Assignment", id.ToString(),
                                cascade.ToMetadata());

        return true;
    }
}
