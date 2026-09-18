namespace LeoClassroom.Persistence.Model;

public class MoodleLink
{
    public long Id { get; set; }
    public long CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public bool Enabled { get; set; }
    public required string MoodleBaseUrl { get; set; }
    public long MoodleCourseId { get; set; }
    public string? TokenCipher { get; set; }
}
