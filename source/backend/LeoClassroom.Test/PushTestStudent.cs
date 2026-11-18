using LeoClassroom.Persistence.Model;
using LeoClassroom.Persistence.Repositories;
using LeoClassroom.Persistence.Util;
using LeoClassroom.Shared;
using NSubstitute;

namespace LeoClassroom.Test;

/// <summary>
///     The student behind the submission the push-consumer tests use: user 50, whose Forgejo login is
///     <see cref="Login" />. Push consumers only count pushes by that account.
/// </summary>
internal static class PushTestStudent
{
    public const long Id = 50;
    public const string Login = "student";

    public static void Register(IUnitOfWork uow)
    {
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(Id).Returns(new ValueTask<User?>(new User
        {
            Id = Id, StudentId = Login, FirstName = "S", LastName = "T", Role = Role.Student
        }));
        uow.UserRepository.Returns(users);
    }
}
