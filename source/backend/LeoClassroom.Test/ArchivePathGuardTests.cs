using LeoClassroom.Services.Forgejo;
using OneOf.Types;

namespace LeoClassroom.Test;

public sealed class ArchivePathGuardTests
{
    [Theory]
    [InlineData("../file.txt")]
    [InlineData("..\\file.txt")]
    [InlineData(".git/config")]
    [InlineData(".GIT/hooks/pre-commit")]
    [InlineData("/project/file.txt")]
    [InlineData("\\project\\file.txt")]
    [InlineData("C:/project/file.txt")]
    public void OriginalPaths_AreRejectedBeforeWrapperDirectoryPromotion(string path)
    {
        ArchiveExtractor.ValidateSourcePaths([path], 16).ShouldBe<ExtractionError>();
    }

    [Fact]
    public void OrdinaryWrapperDirectory_IsAccepted()
    {
        ArchiveExtractor.ValidateSourcePaths(["project/", "project/README.md", "project/src/main.cs"], 16)
                        .ShouldBe<Success>();
    }

    [Fact]
    public void OriginalPathDepth_IsBounded()
    {
        ArchiveExtractor.ValidateSourcePaths(["project/src/main.cs"], 2).ShouldBe<ExtractionError>();
    }
}
