using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

public class CompareEngineTests
{
    private static readonly DateTime Base = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static FileEntry Entry(string relativePath, long length, DateTime timeUtc) =>
        new(@"C:\src\" + relativePath.Replace('/', '\\'), relativePath, length, timeUtc, FileAttributes.Normal);

    private static ScanResult Scan(string root, params FileEntry[] entries) =>
        new(
            root,
            entries.ToDictionary(static e => e.RelativePath, StringComparer.OrdinalIgnoreCase),
            [],
            TimeSpan.Zero,
            DateTime.UtcNow);

    private static CompareResult Run(
        FileEntry[] left,
        FileEntry[] right,
        ConflictBaseline? baseline = null) =>
        new CompareEngine().Compare(Scan(@"C:\src", left), Scan(@"E:\", right), baseline);

    [Fact]
    public void Same_WhenLengthAndTimestampMatch()
    {
        CompareResult result = Run([Entry("a.txt", 10, Base)], [Entry("a.txt", 10, Base)]);

        Assert.Equal(CompareStatus.Same, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void Same_WhenTimestampWithinFatTolerance()
    {
        CompareResult result = Run([Entry("a.txt", 10, Base)], [Entry("a.txt", 10, Base.AddSeconds(1))]);

        Assert.Equal(CompareStatus.Same, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void LeftNewer_WhenLeftIsNewerAndSizeDiffers()
    {
        CompareResult result = Run([Entry("a.txt", 20, Base.AddDays(1))], [Entry("a.txt", 10, Base)]);

        Assert.Equal(CompareStatus.LeftNewer, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void RightNewer_WhenRightIsNewerAndSizeDiffers()
    {
        CompareResult result = Run([Entry("a.txt", 10, Base)], [Entry("a.txt", 20, Base.AddDays(1))]);

        Assert.Equal(CompareStatus.RightNewer, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void LeftOnly_And_RightOnly_AreCounted()
    {
        CompareResult result = Run([Entry("only-left.txt", 1, Base)], [Entry("only-right.txt", 1, Base)]);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.CountOf(CompareStatus.LeftOnly));
        Assert.Equal(1, result.CountOf(CompareStatus.RightOnly));
    }

    [Fact]
    public void Conflict_WhenSizeDiffersInsideToleranceWindow()
    {
        CompareResult result = Run([Entry("a.txt", 10, Base)], [Entry("a.txt", 20, Base)]);

        Assert.Equal(CompareStatus.Conflict, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void Conflict_WhenBothSidesChangedSinceBaseline()
    {
        var baseline = new ConflictBaseline();
        baseline.Set("a.txt", "20:100", "20:100");

        CompareResult result = Run(
            [Entry("a.txt", 30, Base.AddDays(5))],
            [Entry("a.txt", 40, Base.AddDays(6))],
            baseline);

        Assert.Equal(CompareStatus.Conflict, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void NotConflict_WhenOnlyOneSideChangedSinceBaseline()
    {
        FileEntry leftEntry = Entry("a.txt", 30, Base.AddDays(5));
        FileEntry rightEntry = Entry("a.txt", 40, Base.AddDays(6));

        // 基线记录：左侧与当前完全一致（未变），右侧与基线不同（变过）→ 只有一侧变化，不算冲突。
        var baseline = new ConflictBaseline();
        baseline.Set("a.txt", leftEntry.Fingerprint, "1:1");

        CompareResult result = new CompareEngine().Compare(
            Scan(@"C:\src", leftEntry),
            Scan(@"E:\", rightEntry),
            baseline);

        Assert.Equal(CompareStatus.RightNewer, Assert.Single(result.Items).Status);
    }

    [Fact]
    public void PathKeysAreCaseInsensitive()
    {
        CompareResult result = Run([Entry("Docs/ReadMe.md", 5, Base)], [Entry("docs/readme.md", 5, Base)]);

        Assert.Equal(CompareStatus.Same, Assert.Single(result.Items).Status);
    }
}
