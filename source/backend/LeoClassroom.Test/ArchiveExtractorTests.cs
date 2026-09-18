using System.IO.Compression;
using System.Text;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly string _dest = Path.Combine(Path.GetTempPath(), $"leo-extract-test-{Guid.NewGuid():N}");

    public ArchiveExtractorTests() => Directory.CreateDirectory(_dest);

    public void Dispose()
    {
        if (Directory.Exists(_dest))
        {
            Directory.Delete(_dest, recursive: true);
        }
    }

    private static ArchiveExtractor Build(ArchiveLimits? limits = null) =>
        new(Options.Create(limits ?? new ArchiveLimits()), Substitute.For<ILogger<ArchiveExtractor>>());

    private static MemoryStream Zip(params (string Name, byte[] Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using Stream entryStream = entry.Open();
                entryStream.Write(content);
            }
        }
        stream.Position = 0;

        return stream;
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    [Fact]
    public void ValidArchive_ExtractsFiles()
    {
        using MemoryStream zip = Zip(("a.txt", Text("a")), ("dir/b.txt", Text("b")));

        OneOf<Success, ExtractionError> result = Build().ExtractToDirectory(zip, _dest);

        result.ShouldBe<Success>();
        File.Exists(Path.Combine(_dest, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_dest, "dir", "b.txt")).Should().BeTrue();
    }

    [Fact]
    public void SingleTopFolder_IsPromotedToRoot()
    {
        using MemoryStream zip = Zip(("proj/a.txt", Text("a")), ("proj/sub/b.txt", Text("b")));

        Build().ExtractToDirectory(zip, _dest).ShouldBe<Success>();

        File.Exists(Path.Combine(_dest, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_dest, "sub", "b.txt")).Should().BeTrue();
        Directory.Exists(Path.Combine(_dest, "proj")).Should().BeFalse();
    }

    [Fact]
    public void PathTraversal_IsRejected_AndNothingWritten()
    {
        using MemoryStream zip = Zip(("../evil.txt", Text("x")), ("ok.txt", Text("y")));

        OneOf<Success, ExtractionError> result = Build().ExtractToDirectory(zip, _dest);

        result.ShouldBe<ExtractionError>();
        Directory.GetFileSystemEntries(_dest).Should().BeEmpty();
    }

    [Fact]
    public void TopLevelGitDirectory_IsRejected()
    {
        using MemoryStream zip = Zip((".git/config", Text("[core]")), ("a.txt", Text("a")));

        Build().ExtractToDirectory(zip, _dest).ShouldBe<ExtractionError>();
    }

    [Fact]
    public void ZipBomb_ExceedingCompressionRatio_IsRejected()
    {
        using MemoryStream zip = Zip(("zeros.bin", new byte[1024 * 1024]));

        OneOf<Success, ExtractionError> result =
            Build(new ArchiveLimits { MaxCompressionRatio = 5 }).ExtractToDirectory(zip, _dest);

        result.ShouldBe<ExtractionError>();
    }

    [Fact]
    public void ExceedingEntryCount_IsRejected()
    {
        (string, byte[])[] entries = [.. Enumerable.Range(0, 10).Select(i => ($"f{i}.txt", Text("x")))];
        using MemoryStream zip = Zip(entries);

        OneOf<Success, ExtractionError> result =
            Build(new ArchiveLimits { MaxEntryCount = 3 }).ExtractToDirectory(zip, _dest);

        result.ShouldBe<ExtractionError>();
    }

    [Fact]
    public void NestedGitDirectory_IsRejected()
    {
        using MemoryStream zip = Zip(("src/.git/config", Text("[core]")), ("src/a.txt", Text("a")));

        OneOf<Success, ExtractionError> result = Build().ExtractToDirectory(zip, _dest);

        result.ShouldBe<ExtractionError>().Reason.Should().Contain(".git");
        Directory.Exists(Path.Combine(_dest, "src", ".git")).Should().BeFalse();
    }

    [Fact]
    public void EntryLargerThanTheEntryCap_IsRejectedAndNotLeftOnDisk()
    {
        // the cap is enforced against what is actually written, so a header that under-reports the size does not
        // get an oversized file onto the disk
        var limits = new ArchiveLimits { MaxEntryUncompressedBytes = 1024 };
        using MemoryStream zip = Zip(("big.bin", new byte[8 * 1024]));

        OneOf<Success, ExtractionError> result = Build(limits).ExtractToDirectory(zip, _dest);

        result.ShouldBe<ExtractionError>();
        File.Exists(Path.Combine(_dest, "big.bin")).Should().BeFalse();
    }

    [Fact]
    public void TotalLargerThanTheTotalCap_IsRejected()
    {
        var limits = new ArchiveLimits { MaxTotalUncompressedBytes = 4 * 1024 };
        using MemoryStream zip = Zip(
            ("a.bin", new byte[3 * 1024]),
            ("b.bin", new byte[3 * 1024]));

        OneOf<Success, ExtractionError> result = Build(limits).ExtractToDirectory(zip, _dest);

        result.ShouldBe<ExtractionError>();
    }
}
