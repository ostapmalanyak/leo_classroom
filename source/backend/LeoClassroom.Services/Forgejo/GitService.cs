using System.Text;
using System.Text.RegularExpressions;
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
        CancellationToken cancellationToken = default);
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
        string cloneUrl, string targetDirectory, string? checkoutSha, CancellationToken cancellationToken = default)
    {
        if (RejectUnsupportedCloneUrl(cloneUrl) is { } rejected)
        {
            return rejected;
        }

        if (!string.IsNullOrWhiteSpace(checkoutSha) && !ObjectName.IsMatch(checkoutSha))
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

            if (!string.IsNullOrWhiteSpace(checkoutSha))
            {
                OneOf<Success, GitError> checkout = await RunAsync(targetDirectory, withAuth: false, cancellationToken,
                    "checkout", "--detach", checkoutSha);
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
