using LeoClassroom.Services.Util;

namespace LeoClassroom.Services.Download;

public static class DownloadFolderNamer
{
    private const string Placeholder = "student";

    public static string Base(string? lastName, string? firstName, string? studentId = null)
    {
        string last = Slugifier.Slugify(lastName ?? string.Empty);
        string first = Slugifier.Slugify(firstName ?? string.Empty);
        string combined = string.Join('_', new[] { last, first }.Where(part => part.Length > 0));

        return combined.Length > 0 ? combined : Slugifier.Slugify(studentId ?? string.Empty) is { Length: > 0 } id
            ? $"student_{id}"
            : Placeholder;
    }

    public static string Unique(string baseName, ISet<string> used)
    {
        string candidate = baseName;
        int suffix = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{baseName}_{suffix}";
            suffix++;
        }

        return candidate;
    }
}
