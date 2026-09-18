using LeoClassroom.Services.Util;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace LeoClassroom.Services.Forgejo;

public readonly record struct ExtractionError(string Reason);

public interface IArchiveExtractor
{
    public OneOf<Success, ExtractionError> ExtractToDirectory(Stream archive, string destinationRoot);
}

internal sealed class ArchiveExtractor(IOptions<ArchiveLimits> limits, ILogger<ArchiveExtractor> logger)
    : IArchiveExtractor
{
    public OneOf<Success, ExtractionError> ExtractToDirectory(Stream archive, string destinationRoot)
    {
        ArchiveLimits caps = limits.Value;
        string root = Path.GetFullPath(destinationRoot);

        using IArchive opened = ArchiveFactory.OpenArchive(archive, new ReaderOptions());
        List<IArchiveEntry> entries = [.. opened.Entries];

        // Validate the original paths before promoting a wrapper directory. Otherwise '../file',
        // '.git/config', or an absolute path can lose the component that should have rejected it.
        OneOf<Success, ExtractionError> sourcePaths = ValidateSourcePaths(
            entries.Select(entry => entry.Key ?? string.Empty), caps.MaxPathDepth);
        if (sourcePaths.Failure is { } rejectedSource)
        {
            return rejectedSource;
        }

        string? promotionPrefix = ComputePromotionPrefix(entries);

        long totalUncompressed = 0;
        long totalCompressed = 0;
        int fileCount = 0;

        foreach (IArchiveEntry entry in entries)
        {
            string normalized = Normalize(entry.Key ?? string.Empty);
            string relative = StripPrefix(normalized, promotionPrefix);
            if (relative.Length == 0)
            {
                continue;
            }

            OneOf<Success, ExtractionError> pathCheck = ValidatePath(relative, caps.MaxPathDepth);
            if (pathCheck.Failure is { } rejectedEntry)
            {
                return rejectedEntry;
            }

            if (!string.IsNullOrEmpty(entry.LinkTarget))
            {
                return Reject($"archive entry '{relative}' is a link, which is not allowed");
            }

            if (entry.IsDirectory)
            {
                continue;
            }

            fileCount++;
            if (fileCount > caps.MaxEntryCount)
            {
                return Reject($"archive exceeds the maximum entry count of {caps.MaxEntryCount}");
            }
            if (entry.Size > caps.MaxEntryUncompressedBytes)
            {
                return Reject($"archive entry '{relative}' exceeds the maximum entry size");
            }

            totalUncompressed += entry.Size;
            totalCompressed += entry.CompressedSize;
            if (totalUncompressed > caps.MaxTotalUncompressedBytes)
            {
                return Reject("archive exceeds the maximum total uncompressed size");
            }
        }

        if (totalCompressed > 0 && totalUncompressed / Math.Max(1, totalCompressed) > caps.MaxCompressionRatio)
        {
            return Reject("archive exceeds the maximum compression ratio (possible zip bomb)");
        }

        return WriteEntries(entries, promotionPrefix, root, caps);
    }

    private OneOf<Success, ExtractionError> WriteEntries(List<IArchiveEntry> entries, string? promotionPrefix,
                                                         string root, ArchiveLimits caps)
    {
        long written = 0;
        foreach (IArchiveEntry entry in entries.Where(e => !e.IsDirectory))
        {
            string relative = StripPrefix(Normalize(entry.Key ?? string.Empty), promotionPrefix);
            if (relative.Length == 0)
            {
                continue;
            }

            string target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && target != root)
            {
                return Reject($"archive entry '{relative}' escapes the extraction root");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // the caps are enforced against what is actually written, because the sizes declared in the archive
            // header are attacker controlled and the pre-pass can only ever reject an honest archive
            long budget = Math.Min(caps.MaxEntryUncompressedBytes, caps.MaxTotalUncompressedBytes - written);
            long entryBytes;
            using (Stream entryStream = entry.OpenEntryStream())
            {
                using FileStream output = File.Create(target);
                entryBytes = CopyGuarded(entryStream, output, budget);
            }

            written += entryBytes;
            if (entryBytes > budget)
            {
                File.Delete(target);

                return Reject($"archive entry '{relative}' exceeds the allowed uncompressed size");
            }
        }

        return new Success();
    }

    /// <summary>
    ///     Copies at most <paramref name="budget" /> bytes plus one further byte
    /// </summary>
    /// <returns>
    ///     The number of bytes copied. A result greater than <paramref name="budget" /> means the source was
    ///     larger than allowed and the caller has to reject it.
    /// </returns>
    private static long CopyGuarded(Stream source, Stream destination, long budget)
    {
        byte[] buffer = new byte[81920];
        long copied = 0;

        while (copied <= budget)
        {
            // never read more than one byte past the budget, so an oversized entry cannot be written to disk
            int wanted = (int) Math.Min(buffer.Length, budget - copied + 1);
            int read = source.Read(buffer, 0, wanted);
            if (read == 0)
            {
                break;
            }

            destination.Write(buffer, 0, read);
            copied += read;
        }

        return copied;
    }

    private static string? ComputePromotionPrefix(List<IArchiveEntry> entries)
    {
        HashSet<string> topSegments = [];
        bool anyTopLevelFile = false;
        foreach (IArchiveEntry entry in entries)
        {
            string normalized = Normalize(entry.Key ?? string.Empty);
            if (normalized.Length == 0)
            {
                continue;
            }

            int slash = normalized.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                if (!entry.IsDirectory)
                {
                    anyTopLevelFile = true;
                }
                topSegments.Add(normalized);
            }
            else
            {
                topSegments.Add(normalized[..slash]);
            }
        }

        if (topSegments.Count == 1 && !anyTopLevelFile)
        {
            return topSegments.First() + "/";
        }

        return null;
    }

    private static OneOf<Success, ExtractionError> ValidatePath(string relative, int maxDepth)
    {
        if (Path.IsPathRooted(relative) || relative.StartsWith('/') || relative.Contains(':', StringComparison.Ordinal))
        {
            return new ExtractionError($"archive entry '{relative}' uses an absolute path");
        }

        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > maxDepth)
        {
            return new ExtractionError($"archive entry '{relative}' exceeds the maximum path depth");
        }
        if (segments.Any(s => s == ".."))
        {
            return new ExtractionError($"archive entry '{relative}' uses '..' traversal");
        }
        if (segments.Any(s => string.Equals(s, ".git", StringComparison.OrdinalIgnoreCase)))
        {
            return new ExtractionError($"archive entry '{relative}' is inside a .git directory");
        }

        return new Success();
    }

    internal static OneOf<Success, ExtractionError> ValidateSourcePaths(IEnumerable<string> paths, int maxDepth)
    {
        foreach (string path in paths)
        {
            OneOf<Success, ExtractionError> result = ValidatePath(Normalize(path), maxDepth);
            if (result.Failure is { } rejected)
            {
                return rejected;
            }
        }

        return new Success();
    }

    private static string Normalize(string key) => key.Replace('\\', '/');

    private static string StripPrefix(string normalized, string? prefix) =>
        prefix is not null && normalized.StartsWith(prefix, StringComparison.Ordinal)
            ? normalized[prefix.Length..]
            : normalized;

    private OneOf<Success, ExtractionError> Reject(string reason)
    {
        logger.LogWarning("Rejected starter archive: {Reason}", reason);

        return new ExtractionError(reason);
    }
}
