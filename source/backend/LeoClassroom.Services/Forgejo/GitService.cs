using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using CliWrap;
using CliWrap.Buffered;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Forgejo;

public readonly record struct GitError(string Reason);

public interface IGitService
{
    public ValueTask<OneOf<Success, GitError>> SeedRepositoryFromDirectoryAsync(
        string cloneUrl, string sourceDirectory, CancellationToken cancellationToken = default);

    public ValueTask<OneOf<Success, GitError>> OverwriteFileAsync(
        string cloneUrl, string relativePath, string content, string commitMessage,
        CancellationToken cancellationToken = default);

    public ValueTask<OneOf<Success, GitError>> ExportRepositoryAsync(
        string cloneUrl, string targetDirectory, string? checkoutSha,
        CancellationToken cancellationToken = default, Instant? deadline = null);
}

internal sealed partial class GitService(IOptions<ForgejoSettings> settings, ILogger<GitService> logger)
    : IGitService
{
    private const string DefaultBranch = "main";

    /// <summary>
    ///     A git object name: 7 to 64 hexadecimal characters
    /// </summary>
    /// <remarks>
    ///     Commit ids reach this service from stored webhook payloads. Anything else - a leading dash above all -
    ///     would be read by git as an option rather than a revision, so it is rejected outright.
    /// </remarks>
    [GeneratedRegex("^[0-9a-fA-F]{7,64}$")]
    private static partial Regex ObjectName { get; }

    /// <summary>
    ///     Rejects anything but an absolute http(s) URL
    /// </summary>
    /// <remarks>
    ///     git happily accepts transports such as <c>ext::</c>, which executes an arbitrary helper command, and
    ///     <c>file://</c>, which would read the server's own filesystem. Neither may ever be handed to git here.
    /// </remarks>
    private static bool IsSupportedCloneUrl(string cloneUrl) =>
        Uri.TryCreate(cloneUrl, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    public async ValueTask<OneOf<Success, GitError>> SeedRepositoryFromDirectoryAsync(
        string cloneUrl, string sourceDirectory, CancellationToken cancellationToken = default)
    {
        if (RejectUnsupportedCloneUrl(cloneUrl) is { } rejected)
        {
            return rejected;
        }

        return await InWorkspaceAsync(async workDir =>
        {
            OneOf<Success, GitError> clone = await CloneAsync(cloneUrl, workDir, cancellationToken);
            if (clone.Failure is { } cloneFailed)
            {
                return cloneFailed;
            }

            CopyDirectory(sourceDirectory, workDir);

            return await StageCommitPushAsync(cloneUrl, workDir, "Seed starter material", cancellationToken);
        });
    }

    public async ValueTask<OneOf<Success, GitError>> OverwriteFileAsync(
        string cloneUrl, string relativePath, string content, string commitMessage,
        CancellationToken cancellationToken = default)
    {
        if (RejectUnsupportedCloneUrl(cloneUrl) is { } rejected)
        {
            return rejected;
        }

        // Use the same separators for validation, symlink checks, and the eventual write on every OS.
        relativePath = relativePath.Replace('\\', '/');
        if (!RepositoryFilePath.IsSafe(relativePath))
        {
            return new GitError("the file path is not inside the repository");
        }

        return await InWorkspaceAsync(async workDir =>
        {
            OneOf<Success, GitError> clone = await CloneAsync(cloneUrl, workDir, cancellationToken);
            if (clone.Failure is { } cloneFailed)
            {
                return cloneFailed;
            }

            // A copied/forked repository can contain a README symlink to a server file or .git/config.
            // Lexical traversal checks alone do not make following that link safe.
            if (RepositoryFilePath.ContainsLink(workDir, relativePath))
            {
                return new GitError("the file path contains a symbolic link");
            }

            string target = Path.Combine(workDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, content, cancellationToken);

            return await StageCommitPushAsync(cloneUrl, workDir, commitMessage, cancellationToken);
        });
    }

    public async ValueTask<OneOf<Success, GitError>> ExportRepositoryAsync(
        string cloneUrl, string targetDirectory, string? checkoutSha,
        CancellationToken cancellationToken = default, Instant? deadline = null)
    {
        if (RejectUnsupportedCloneUrl(cloneUrl) is { } rejected)
        {
            return rejected;
        }

        cloneUrl = InternalCloneUrl(cloneUrl);

        if (deadline is null && !string.IsNullOrWhiteSpace(checkoutSha) && !ObjectName.IsMatch(checkoutSha))
        {
            logger.LogWarning("Refused to check out {Sha}, which is not a git object name", checkoutSha);

            return new GitError("the recorded commit id is not a git object name");
        }

        try
        {
            OneOf<Success, GitError> clone = await RunAsync(workingDirectory: null, withAuth: true, cancellationToken,
                "clone", cloneUrl, targetDirectory);
            if (clone.Failure is { } cloneFailed)
            {
                return cloneFailed;
            }

            string? revision = checkoutSha;
            if (deadline is not null)
            {
                OneOf<string, GitError> selected = await FindRevisionAtOrBeforeAsync(
                    targetDirectory, deadline.Value, cancellationToken);
                OneOf<Success, GitError> selection = selected.Match<OneOf<Success, GitError>>(
                    value =>
                    {
                        revision = value;
                        return new Success();
                    },
                    error => error);
                if (selection.Failure is { } selectionFailed)
                {
                    return selectionFailed;
                }
            }

            if (!string.IsNullOrWhiteSpace(revision))
            {
                OneOf<Success, GitError> checkout = await RunAsync(targetDirectory, withAuth: false, cancellationToken,
                    "checkout", "--detach", revision);
                if (checkout.Failure is { } checkoutFailed)
                {
                    return checkoutFailed;
                }
            }

            // Drop the origin remote so the packaged .git carries no service credentials.
            return await RunAsync(targetDirectory, withAuth: false, cancellationToken, "remote", "remove", "origin");
        }
        catch (Exception ex) when (ex is IOException or CliWrap.Exceptions.CommandExecutionException)
        {
            logger.LogError(ex, "Git export failed");

            return new GitError(ex.Message);
        }
    }

    private async ValueTask<OneOf<string, GitError>> FindRevisionAtOrBeforeAsync(
        string repositoryDirectory, Instant deadline, CancellationToken cancellationToken)
    {
        BufferedCommandResult result = await RunBufferedAsync(repositoryDirectory, withAuth: false, cancellationToken,
            "log", "--all", "--format=%H%x09%cI");
        if (result.ExitCode != 0)
        {
            return new GitError("git log failed with exit code " + result.ExitCode);
        }

        var commits = new List<(string Sha, Instant At)>();
        foreach (string line in result.StandardOutput.Split(
                     ['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t', 2);
            if (fields.Length != 2 || !ObjectName.IsMatch(fields[0])
                || !DateTimeOffset.TryParse(fields[1], CultureInfo.InvariantCulture,
                                             DateTimeStyles.RoundtripKind, out DateTimeOffset commitTime))
            {
                continue;
            }

            commits.Add((fields[0], Instant.FromDateTimeOffset(commitTime)));
        }

        foreach ((string sha, Instant at) in commits.OrderByDescending(commit => commit.At))
        {
            if (at <= deadline)
            {
                return sha;
            }
        }

        return new GitError("no repository commit exists before the deadline");
    }

    internal string InternalCloneUrl(string cloneUrl)
    {
        Uri cloneUri = new(cloneUrl, UriKind.Absolute);
        Uri forgejoUri = new(settings.Value.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var rewritten = new UriBuilder(forgejoUri)
        {
            Path = cloneUri.AbsolutePath,
            Query = cloneUri.Query.TrimStart('?'),
            Fragment = cloneUri.Fragment.TrimStart('#')
        };

        return rewritten.Uri.AbsoluteUri;
    }

    private async ValueTask<OneOf<Success, GitError>> CloneAsync(
        string cloneUrl, string workDir, CancellationToken cancellationToken) =>
        await RunAsync(workingDirectory: null, withAuth: true, cancellationToken,
                       "-c", $"init.defaultBranch={DefaultBranch}", "clone", cloneUrl, workDir);

    private async ValueTask<OneOf<Success, GitError>> StageCommitPushAsync(
        string cloneUrl, string workDir, string commitMessage, CancellationToken cancellationToken)
    {
        OneOf<Success, GitError> add = await RunAsync(workDir, withAuth: false, cancellationToken, "add", "-A");
        if (add.Failure is { } addFailed)
        {
            return addFailed;
        }

        OneOf<Success, GitError> commit = await RunAsync(workDir, withAuth: false, cancellationToken,
            "-c", $"user.name={settings.Value.BotName}", "-c", $"user.email={settings.Value.BotEmail}",
            "commit", "--allow-empty", "-m", commitMessage);
        if (commit.Failure is { } commitFailed)
        {
            return commitFailed;
        }

        return await RunAsync(workDir, withAuth: true, cancellationToken, "push", "origin", $"HEAD:{DefaultBranch}");
    }

    private async ValueTask<OneOf<Success, GitError>> RunAsync(
        string? workingDirectory, bool withAuth, CancellationToken cancellationToken, params string[] args)
    {
        logger.LogDebug("Running git {Args}", string.Join(' ', args));

        BufferedCommandResult result = await Cli.Wrap("git")
            .WithArguments(args)
            .WithEnvironmentVariables(BuildEnvironment(withAuth))
            .WithWorkingDirectory(workingDirectory ?? Directory.GetCurrentDirectory())
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);

        if (result.ExitCode != 0)
        {
            logger.LogWarning("git {Args} exited {Code}: {Error}", string.Join(' ', args), result.ExitCode,
                              result.StandardError);

            return new GitError($"git {args[0]} failed with exit code {result.ExitCode}");
        }

        return new Success();
    }

    private async ValueTask<BufferedCommandResult> RunBufferedAsync(
        string? workingDirectory, bool withAuth, CancellationToken cancellationToken, params string[] args)
    {
        logger.LogDebug("Running git {Args}", string.Join(' ', args));

        return await Cli.Wrap("git")
            .WithArguments(args)
            .WithEnvironmentVariables(BuildEnvironment(withAuth))
            .WithWorkingDirectory(workingDirectory ?? Directory.GetCurrentDirectory())
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);
    }

    /// <summary>
    ///     Builds the environment for one git invocation
    /// </summary>
    /// <remarks>
    ///     The service credential is passed through <c>GIT_CONFIG_*</c> rather than <c>git -c</c>: a command line
    ///     is world-readable through <c>/proc/&lt;pid&gt;/cmdline</c>, so an admin token on it leaks to every other
    ///     process on the host. The environment of a process is not.
    /// </remarks>
    private Dictionary<string, string?> BuildEnvironment(bool withAuth)
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // never block a background worker on an interactive credential or host-key prompt
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_ASKPASS"] = string.Empty,
            ["GCM_INTERACTIVE"] = "never"
        };

        if (!withAuth)
        {
            return environment;
        }

        environment["GIT_CONFIG_COUNT"] = "1";
        environment["GIT_CONFIG_KEY_0"] = "http.extraHeader";
        environment["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {BasicCredential()}";

        return environment;
    }

    private string BasicCredential()
    {
        string raw = $"{settings.Value.BotName}:{settings.Value.AdminToken}";

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private OneOf<Success, GitError>? RejectUnsupportedCloneUrl(string cloneUrl)
    {
        if (IsSupportedCloneUrl(cloneUrl))
        {
            return null;
        }

        logger.LogWarning("Refused a git operation against an unsupported clone URL");

        return new GitError("the repository URL is not an absolute http(s) URL");
    }

    private async ValueTask<OneOf<Success, GitError>> InWorkspaceAsync(
        Func<string, ValueTask<OneOf<Success, GitError>>> work)
    {
        string workDir = Path.Combine(Path.GetTempPath(), $"leo-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            return await work(workDir);
        }
        catch (Exception ex) when (ex is IOException or CliWrap.Exceptions.CommandExecutionException)
        {
            logger.LogError(ex, "Git workspace operation failed");

            return new GitError(ex.Message);
        }
        finally
        {
            TryDelete(workDir);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination, StringComparison.Ordinal));
        }
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal), overwrite: true);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not remove temporary git workspace {Path}", path);
        }
    }
}
