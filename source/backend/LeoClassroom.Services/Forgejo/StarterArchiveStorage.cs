using System.IO.Compression;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Options;
using SharpCompress.Common;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Services.Forgejo;

public sealed record StarterArchiveFile(string Id, string Path, string FileName,
                                        IReadOnlyCollection<string> Entries);

public sealed record StarterArchiveUploadError(string Message);

public sealed record StarterArchiveDownload(string Path, string FileName);

public interface IStarterArchiveStorage
{
    public ValueTask<OneOf<StarterArchiveFile, StarterArchiveUploadError>> StoreAsync(
        Stream source, string fileName, CancellationToken cancellationToken);

    public StarterArchiveFile? RetainPending(string id);

    public StarterArchiveDownload? Find(string? archivePath, string downloadFileName);

    public void Delete(string archivePath);
}

internal sealed class StarterArchiveStorage(
    IOptions<StarterArchiveSettings> settings,
    IArchiveExtractor archiveExtractor,
    IClock clock) : IStarterArchiveStorage
{
    private const string InvalidArchiveMessage = "The ZIP archive is invalid or contains unsafe content.";
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromHours(24);

    public async ValueTask<OneOf<StarterArchiveFile, StarterArchiveUploadError>> StoreAsync(
        Stream source, string fileName, CancellationToken cancellationToken)
    {
        StarterArchiveSettings configured = settings.Value;
        if (!fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return new StarterArchiveUploadError("Select a ZIP archive.");
        }

        string safeFileName = Path.GetFileName(fileName.Replace('\\', '/'));
        if (safeFileName.Length == 0)
        {
            return new StarterArchiveUploadError("Select a ZIP archive.");
        }

        string root = Path.GetFullPath(configured.StoragePath);
        string pendingRoot = Path.Combine(root, "pending");
        string assignmentsRoot = Path.Combine(root, "assignments");
        Directory.CreateDirectory(pendingRoot);
        Directory.CreateDirectory(assignmentsRoot);
        RemoveExpiredPendingArchives(pendingRoot, clock.GetCurrentInstant());

        string id = Guid.NewGuid().ToString("N");
        string pendingPath = PendingPath(pendingRoot, id);
        bool keepPending = false;
        try
        {
            await using (var destination = new FileStream(
                             pendingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[81920];
                long written = 0;
                while (true)
                {
                    int count = await source.ReadAsync(buffer, cancellationToken);
                    if (count == 0)
                    {
                        break;
                    }

                    written += count;
                    if (written > configured.MaxUploadBytes)
                    {
                        long maximumMegabytes = (configured.MaxUploadBytes + 1024 * 1024 - 1) / (1024 * 1024);
                        return new StarterArchiveUploadError(
                            $"The ZIP archive exceeds the {maximumMegabytes} MB upload limit.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }

            if (!IsZipFile(pendingPath))
            {
                return new StarterArchiveUploadError(InvalidArchiveMessage);
            }

            using var archive = File.OpenRead(pendingPath);
            OneOf<ArchiveContentPreview, ExtractionError> preview = archiveExtractor.Preview(archive);
            return preview.Match<OneOf<StarterArchiveFile, StarterArchiveUploadError>>(
                contents =>
                {
                    if (!contents.Entries.Any(entry => !entry.EndsWith('/', StringComparison.Ordinal)))
                    {
                        return new StarterArchiveUploadError("The ZIP archive contains no files.");
                    }

                    keepPending = true;

                    return new StarterArchiveFile(id, pendingPath, safeFileName, contents.Entries);
                },
                _ => new StarterArchiveUploadError(InvalidArchiveMessage));
        }
        catch (InvalidDataException)
        {
            return new StarterArchiveUploadError(InvalidArchiveMessage);
        }
        catch (EndOfStreamException)
        {
            return new StarterArchiveUploadError(InvalidArchiveMessage);
        }
        catch (ArchiveOperationException)
        {
            return new StarterArchiveUploadError(InvalidArchiveMessage);
        }
        finally
        {
            if (!keepPending && File.Exists(pendingPath))
            {
                File.Delete(pendingPath);
            }
        }
    }

    public StarterArchiveFile? RetainPending(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            return null;
        }

        string root = Path.GetFullPath(settings.Value.StoragePath);
        string pendingPath = PendingPath(Path.Combine(root, "pending"), id);
        if (!File.Exists(pendingPath))
        {
            return null;
        }

        if (IsExpired(pendingPath, clock.GetCurrentInstant()))
        {
            File.Delete(pendingPath);

            return null;
        }

        string assignmentsRoot = Path.Combine(root, "assignments");
        Directory.CreateDirectory(assignmentsRoot);
        string retainedPath = Path.Combine(assignmentsRoot, $"{id}.zip");
        File.Move(pendingPath, retainedPath);

        return new StarterArchiveFile(id, retainedPath, "starter.zip", []);
    }

    public StarterArchiveDownload? Find(string? archivePath, string downloadFileName)
    {
        if (archivePath is null)
        {
            return null;
        }

        string root = Path.GetFullPath(Path.Combine(settings.Value.StoragePath, "assignments"));
        string fullPath = Path.GetFullPath(archivePath);
        string relativePath = Path.GetRelativePath(root, fullPath);
        if (relativePath == "." || Path.IsPathRooted(relativePath)
                                 || relativePath == ".."
                                 || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}",
                                                            StringComparison.Ordinal)
                                 || !File.Exists(fullPath))
        {
            return null;
        }

        return new StarterArchiveDownload(fullPath, downloadFileName);
    }

    public void Delete(string archivePath)
    {
        StarterArchiveDownload? stored = Find(archivePath, Path.GetFileName(archivePath));
        if (stored is not null)
        {
            File.Delete(stored.Path);
        }
    }

    private static string PendingPath(string pendingRoot, string id) => Path.Combine(pendingRoot, $"{id}.zip");

    private static void RemoveExpiredPendingArchives(string pendingRoot, Instant now)
    {
        foreach (string path in Directory.EnumerateFiles(pendingRoot, "*.zip"))
        {
            if (IsExpired(path, now))
            {
                File.Delete(path);
            }
        }
    }

    private static bool IsExpired(string path, Instant now) =>
        File.GetLastWriteTimeUtc(path) < (now - Duration.FromTimeSpan(PendingLifetime)).ToDateTimeUtc();

    private static bool IsZipFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[4];
        int read = stream.Read(signature);

        return read == signature.Length
               && signature[0] == 0x50
               && signature[1] == 0x4B
               && (signature[2], signature[3]) is (0x03, 0x04) or (0x05, 0x06) or (0x07, 0x08);
    }
}
