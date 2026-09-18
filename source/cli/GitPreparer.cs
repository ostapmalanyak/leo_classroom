using System.Diagnostics;

namespace LeoClassroom.Cli;

// What to do when the working directory already contains a .git.
public enum ExistingGitChoice
{
    PushAsIs,
    Recreate
}

public sealed record GitError(string Reason);

public static class GitPreparer
{
    // OS/editor cruft plus the tool's own footprint that must never be pushed.
    public static IReadOnlyList<string> ComputeExcludes(string? toolFileName)
    {
        var excludes = new List<string>
        {
            ".DS_Store",
            "Thumbs.db",
            "leo-cli",
            "leo-cli.exe",
            ".leo/"
        };
        if (!string.IsNullOrWhiteSpace(toolFileName))
        {
            excludes.Add(toolFileName);
        }

        return excludes;
    }

    public static bool HasGit(string directory) => Directory.Exists(Path.Combine(directory, ".git"));

    // Preserve project rules (especially secrets and build output) when adding the tool's exclusions.
    public static void WriteExcludes(string directory, IReadOnlyList<string> excludes)
    {
        string path = Path.Combine(directory, ".gitignore");
        string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var rules = new HashSet<string>(existing.Split('\n').Select(line => line.TrimEnd('\r')),
                                        StringComparer.Ordinal);
        string[] missing = excludes.Where(rule => rules.Add(rule)).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        string separator = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : string.Empty;
        File.AppendAllText(path, separator + string.Join('\n', missing) + "\n");
    }
}

// Thin wrapper over the `git` binary on PATH. The push uses the SSO git credential helper; no token is ever
// written into a URL passed here.
public sealed class GitRunner(string workingDirectory)
{
    public GitError? Run(params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            // Leave stdout attached to the console: an unread redirected pipe can fill and deadlock git.
            RedirectStandardOutput = false,
            UseShellExecute = false
        };
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new GitError("could not start git (is it installed and on PATH?)");
        }
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0 ? null : new GitError($"git {args[0]} failed: {stderr.Trim()}");
    }
}
