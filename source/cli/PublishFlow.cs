using System.Text;

namespace LeoClassroom.Cli;

// Orchestrates the interactive publish: pick course → prompts → confirm → prepare git → push → create
// assignment → print accept link. Step-wise and recoverable; reuses only teacher-authorized backend contracts.
public sealed class PublishFlow(CliConfig config, BackendClient backend, string username)
{
    public async Task<int> RunAsync(string workingDirectory, CancellationToken ct)
    {
        CourseOverviewDto[]? courses = await backend.GetCoursesAsync(ct);
        if (courses is null || courses.Length == 0)
        {
            Console.WriteLine("No courses available for your account.");

            return 1;
        }

        Console.WriteLine("Your courses:");
        for (int i = 0; i < courses.Length; i++)
        {
            Console.WriteLine($"  {i + 1}) {courses[i].Title} ({courses[i].RosterName})");
        }
        CourseOverviewDto course = courses[Prompts.ReadChoice("Select a course", courses.Length) - 1];

        string title = Prompts.ReadRequired("Assignment title");
        DeadlineKind kind = Prompts.ReadDeadlineKind();
        (string? deadlineIso, bool revokesRead) = ReadDeadline(kind);
        string? instructions = Prompts.ReadOptional("Markdown instructions");

        string repoSlug = Slugify(title);
        string repoUrl = $"{config.ForgejoBaseUrl.TrimEnd('/')}/{username}/{repoSlug}.git";

        Console.WriteLine();
        Console.WriteLine("About to publish:");
        Console.WriteLine($"  Course:      {course.Title}");
        Console.WriteLine($"  Title:       {title}");
        Console.WriteLine($"  Deadline:    {(deadlineIso is null ? "none" : $"{deadlineIso} ({kind})")}");
        Console.WriteLine($"  Source repo: {repoUrl}");
        if (!Prompts.ReadYesNo("Proceed?", false))
        {
            Console.WriteLine("Cancelled. No changes made.");

            return 0;
        }

        return await PublishAsync(workingDirectory, course.Id, title, instructions, deadlineIso, kind, revokesRead,
                                  repoUrl, ct);
    }

    private async Task<int> PublishAsync(
        string workingDirectory, long courseId, string title, string? instructions, string? deadlineIso,
        DeadlineKind kind, bool revokesRead, string repoUrl, CancellationToken ct)
    {
        var done = new List<string>();

        GitError? gitError = PrepareGit(workingDirectory, repoUrl);
        if (gitError is not null)
        {
            return Fail(done, $"git preparation/push failed: {gitError.Reason}");
        }
        done.Add("source repository pushed to Forgejo");

        var request = new AssignmentCreateRequestDto(
            courseId, title, null, instructions, deadlineIso, kind.ToString(), revokesRead,
            "CopyRepo", repoUrl, instructions, false, null, "Deadline");
        (AssignmentDto? created, BackendError? error) = await backend.CreateAssignmentAsync(request, ct);
        if (created is null)
        {
            return Fail(done, error!.Reason);
        }
        done.Add($"assignment created (id {created.Id})");

        AcceptLinkDto? link = await backend.GetAcceptLinkAsync(created.Id, ct);

        Console.WriteLine();
        Console.WriteLine("Published successfully:");
        Console.WriteLine($"  Repository:  {repoUrl}");
        Console.WriteLine($"  Accept link: {link?.AcceptLink ?? "(open the assignment in the web app)"}");

        return 0;
    }

    private static GitError? PrepareGit(string dir, string repoUrl)
    {
        IReadOnlyList<string> excludes = GitPreparer.ComputeExcludes(ResolveToolFileName());
        GitPreparer.WriteExcludes(dir, excludes);
        var git = new GitRunner(dir);

        if (GitPreparer.HasGit(dir))
        {
            Console.WriteLine("This folder already contains a git repository.");
            Console.WriteLine("  1) Push the existing repository/history as-is");
            Console.WriteLine("  2) Delete .git and start a fresh repository");
            ExistingGitChoice choice = Prompts.ReadChoice("Choose", 2) == 1
                ? ExistingGitChoice.PushAsIs
                : ExistingGitChoice.Recreate;

            if (choice == ExistingGitChoice.Recreate)
            {
                Directory.Delete(Path.Combine(dir, ".git"), recursive: true);
                GitError? init = InitAndCommit(git);
                if (init is not null)
                {
                    return init;
                }
            }
        }
        else
        {
            GitError? init = InitAndCommit(git);
            if (init is not null)
            {
                return init;
            }
        }

        // Push over HTTPS using the configured git credential helper (no token in the URL).
        git.Run("remote", "remove", "origin");

        return git.Run("remote", "add", "origin", repoUrl)
               ?? git.Run("push", "-u", "origin", "HEAD:main");
    }

    private static GitError? InitAndCommit(GitRunner git) =>
        git.Run("init", "-b", "main")
        ?? git.Run("add", "-A")
        ?? git.Run("commit", "-m", "Initial assignment material");

    private static (string? DeadlineIso, bool RevokesRead) ReadDeadline(DeadlineKind kind)
    {
        if (kind == DeadlineKind.None)
        {
            return (null, false);
        }

        while (true)
        {
            string input = Prompts.ReadRequired("Deadline (yyyy-MM-dd HH:mm)");
            if (DeadlineParser.TryParse(input, CliConfig.SchoolTimeZone, out string iso, out string error))
            {
                bool revokes = kind == DeadlineKind.Hard
                               && Prompts.ReadYesNo("Hard deadline revokes read access?", false);

                return (iso, revokes);
            }
            Console.WriteLine($"  {error}");
        }
    }

    private static int Fail(IReadOnlyList<string> done, string reason)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Publish failed: {reason}");
        if (done.Count > 0)
        {
            Console.Error.WriteLine("Completed steps (safe to re-run with the same inputs):");
            foreach (string step in done)
            {
                Console.Error.WriteLine($"  - {step}");
            }
        }

        return 1;
    }

    private static string? ResolveToolFileName()
    {
        try
        {
            return Path.GetFileName(Environment.ProcessPath);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool lastDash = false;
        foreach (char c in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastDash = false;
            }
            else if (!lastDash && builder.Length > 0)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "assignment";
    }
}
