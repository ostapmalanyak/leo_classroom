namespace LeoClassroom.Services.Forgejo;

internal static class RepositoryFilePath
{
    public static bool IsSafe(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)
            || relativePath.StartsWith('\\') || relativePath.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        string[] segments = relativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length > 0 && !segments.Any(segment => segment is "." or ".."
            || string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase));
    }

    public static bool ContainsLink(string root, string relativePath)
    {
        string current = root;
        foreach (string segment in relativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
        }

        return false;
    }
}
