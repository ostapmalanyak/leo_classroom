using LeoClassroom.Shared;

namespace LeoClassroom.Persistence.Model;

public class Roster
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public RosterKind Kind { get; set; }
    public string? ClassKey { get; set; }
    public long? OwnerId { get; set; }
    public User? Owner { get; set; }
    public ICollection<User> Members { get; set; } = [];
}
