using LeoClassroom.Services.Util;

namespace LeoClassroom.Services.Forgejo;

public static class ForgejoNaming
{
    public const string TeachersTeamName = "teachers";
    public const string FeedbackBranchName = "feedback";
    public const string DefaultBranchName = "main";

    public static string OrgName(string courseTitle, long courseId) =>
        $"{Slugifier.Slugify(courseTitle)}-{courseId}";

    public static string RepoName(string courseTitle, string assignmentSlug, string studentId) =>
        $"{Slugifier.Slugify(courseTitle)}-{assignmentSlug}-{studentId}";
}
