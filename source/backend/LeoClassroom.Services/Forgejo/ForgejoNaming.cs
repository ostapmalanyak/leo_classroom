using LeoClassroom.Services.Util;

namespace LeoClassroom.Services.Forgejo;

public static class ForgejoNaming
{
    private const int MaxOrgNameLength = 40;

    public const string TeachersTeamName = "teachers";
    public const string FeedbackBranchName = "feedback";
    public const string DefaultBranchName = "main";

    public static string OrgName(string courseTitle, long courseId)
    {
        string suffix = $"-{courseId}";
        string titleSlug = Slugifier.Slugify(courseTitle);
        if (titleSlug.Length == 0)
        {
            titleSlug = "course";
        }

        int titleLength = Math.Min(titleSlug.Length, MaxOrgNameLength - suffix.Length);

        return $"{titleSlug[..titleLength].TrimEnd('-')}{suffix}";
    }

    public static string RepoName(string courseTitle, string assignmentSlug, string studentId) =>
        $"{Slugifier.Slugify(courseTitle)}-{assignmentSlug}-{studentId}";

    /// <summary>Family name first, no space: "Mukhin" + "Oleksandr" -> "MukhinOleksandr".</summary>
    public static string UserDisplayName(string givenName, string familyName) =>
        familyName.Trim() + givenName.Trim();
}
