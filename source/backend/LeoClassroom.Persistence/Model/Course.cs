namespace LeoClassroom.Persistence.Model;

public class Course
{
    public long Id { get; set; }
    public required string Title { get; set; }
    public long RosterId { get; set; }
    public Roster Roster { get; set; } = null!;
    public long OwnerId { get; set; }
    public User Owner { get; set; } = null!;
    public ICollection<User> CoTeachers { get; set; } = [];
    public bool IsReadOnly { get; set; }
    public bool StudentsRetainAccess { get; set; }
    public string? ForgejoOrg { get; set; }
    public Instant CreatedAt { get; set; }
}
