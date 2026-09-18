using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;

namespace LeoClassroom.Services;

public interface IUserDirectoryService
{
    public ValueTask<IReadOnlyCollection<UserSummary>> SearchAsync(string term, Role? role);
}

internal sealed class UserDirectoryService(IUnitOfWork uow) : IUserDirectoryService
{
    private const int MaxResults = 20;

    public async ValueTask<IReadOnlyCollection<UserSummary>> SearchAsync(string term, Role? role) =>
        await uow.UserRepository.SearchActiveAsync(term ?? string.Empty, role, MaxResults);
}
