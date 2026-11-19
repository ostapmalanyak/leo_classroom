using System.IO.Compression;
using System.Text;
using LeoClassroom.Services.Forgejo;
using LeoClassroom.Services.Util;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace LeoClassroom.Test;

public sealed class StarterArchiveStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"leo-starter-storage-{Guid.NewGuid():N}");
    private readonly IClock _clock = Substitute.For<IClock>();

    public StarterArchiveStorageTests() =>
        _clock.GetCurrentInstant().Returns(Instant.FromUtc(2026, 6, 13, 12, 0));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task StoreAsync_PreviewsAndRetainsValidatedArchive()
    {
        var storage = Build();
        using MemoryStream zip = Zip(("project/src/main.cs", "class Main {}"));

        StarterArchiveFile staged = (await storage.StoreAsync(zip, "project.zip", CancellationToken.None))
            .ShouldBe<StarterArchiveFile>();
        staged.Entries.Should().Contain("src/main.cs");
        File.Exists(staged.Path).Should().BeTrue();

        StarterArchiveFile retained = storage.RetainPending(staged.Id)!;
        retained.Path.Should().Be(Path.Combine(_root, "assignments", $"{staged.Id}.zip"));
        File.Exists(Path.Combine(_root, "pending", $"{staged.Id}.zip")).Should().BeFalse();
        storage.Find(retained.Path, "sorting-starter.zip").Should().Be(
            new StarterArchiveDownload(retained.Path, "sorting-starter.zip"));

        storage.Delete(retained.Path);
        File.Exists(retained.Path).Should().BeFalse();
    }

    [Fact]
    public async Task StoreAsync_RejectsWrongExtensionAndOversizedUpload()
    {
        var storage = Build(maxUploadBytes: 16);
        using MemoryStream smallZip = Zip(("a.txt", "a"));

        (await storage.StoreAsync(smallZip, "starter.tar", CancellationToken.None))
            .ShouldBe<StarterArchiveUploadError>().Message.Should().Be("Select a ZIP archive.");

        using MemoryStream largeZip = Zip(("a.txt", new string('a', 256)));
        (await storage.StoreAsync(largeZip, "starter.zip", CancellationToken.None))
            .ShouldBe<StarterArchiveUploadError>().Message.Should().Contain("upload limit");
        Directory.GetFiles(_root, "*.zip", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_RejectsArchiveWithoutFiles()
    {
        var storage = Build();
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("empty/");
        }
        zip.Position = 0;

        (await storage.StoreAsync(zip, "empty.zip", CancellationToken.None))
            .ShouldBe<StarterArchiveUploadError>().Message.Should().Be("The ZIP archive contains no files.");
    }

    [Fact]
    public async Task StoreAsync_RejectsTruncatedZipSignature()
    {
        var storage = Build();
        using var zip = new MemoryStream([0x50, 0x4b, 0x03, 0x04]);

        var result = await storage.StoreAsync(zip, "broken.zip", CancellationToken.None);

        result.ShouldBe<StarterArchiveUploadError>();
    }

    [Fact]
    public async Task RetainPending_RejectsExpiredUpload()
    {
        var storage = Build();
        using MemoryStream zip = Zip(("a.txt", "a"));
        StarterArchiveFile staged = (await storage.StoreAsync(zip, "starter.zip", CancellationToken.None))
            .ShouldBe<StarterArchiveFile>();
        File.SetLastWriteTimeUtc(staged.Path, new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc));

        storage.RetainPending(staged.Id).Should().BeNull();
        File.Exists(staged.Path).Should().BeFalse();
    }

    private StarterArchiveStorage Build(long maxUploadBytes = 1024 * 1024) =>
        new(Options.Create(new StarterArchiveSettings { StoragePath = _root, MaxUploadBytes = maxUploadBytes }),
            new ArchiveExtractor(Options.Create(new ArchiveLimits()), Substitute.For<ILogger<ArchiveExtractor>>()),
            _clock);

    private static MemoryStream Zip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                using Stream entry = archive.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        stream.Position = 0;

        return stream;
    }

}
