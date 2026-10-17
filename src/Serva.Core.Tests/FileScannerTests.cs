using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.Core.Tests;

public class FileScannerTests
{
    [Fact]
    public void ScansNestedFilesWithRelativePaths()
    {
        using var tree = new TestTree("scan");
        tree.Add("a.txt", "A");
        tree.Add("docs/b.txt", "BB");

        ScanResult result = new FileScanner(new ExcludeMatcher()).Scan(tree.Root);

        Assert.Equal(2, result.FileCount);
        Assert.Equal(3, result.TotalBytes);
        Assert.Contains("docs/b.txt", result.Files.Keys);
        Assert.Contains("docs", result.Directories);
        Assert.False(result.IsFromSnapshot);
    }

    [Fact]
    public void SkipsExcludedDirectoriesAndFiles()
    {
        using var tree = new TestTree("scan-exclude");
        tree.Add("keep.txt", "K");
        tree.Add("_ServaTrash/old.txt", "OLD");
        tree.Add("Thumbs.db", "THUMB");
        tree.Add("node_modules/pkg/x.js", "X");

        // 自定义规则是在「默认规则」之上追加（应用里 SyncOptions.ExcludePatterns 亦以默认值为起点）。
        var matcher = new ExcludeMatcher([.. ExcludeMatcher.DefaultExcludePatterns, "node_modules"]);
        ScanResult result = new FileScanner(matcher).Scan(tree.Root);

        Assert.Single(result.Files);
        Assert.True(result.Files.ContainsKey("keep.txt"));
    }

    [Fact]
    public void SingleFileSourceUsesFileNameAsKey()
    {
        using var tree = new TestTree("scan-single");
        string file = tree.Add("发票.xlsx", "X");

        ScanResult result = new FileScanner(new ExcludeMatcher()).Scan(file);

        Assert.Equal(1, result.FileCount);
        Assert.True(result.Files.ContainsKey("发票.xlsx"));
    }

    [Fact]
    public void ScanManyFlagsCollisions()
    {
        using var first = new TestTree("many-a");
        using var second = new TestTree("many-b");
        first.Add("same.txt", "1");
        first.Add("only-a.txt", "A");
        second.Add("same.txt", "2");
        second.Add("only-b.txt", "B");

        ScanResult result = new FileScanner(new ExcludeMatcher())
            .ScanMany([first.Root, second.Root], "本机来源");

        Assert.Equal(3, result.FileCount);
        Assert.Single(result.Collisions);
        Assert.Equal("same.txt", result.Collisions[0]);
    }

    [Fact]
    public void SnapshotIsUsedWhenDirectoriesAreUnchanged()
    {
        using var tree = new TestTree("scan-snapshot");
        tree.Add("a.txt", "A");

        var store = new SnapshotStore(SyncFixture.NewTempDirectory("state"));
        var scanner = new FileScanner(new ExcludeMatcher(), snapshotStore: store);

        ScanResult first = scanner.Scan(tree.Root, "TESTSERIAL");
        ScanResult second = scanner.Scan(tree.Root, "TESTSERIAL");

        Assert.False(first.IsFromSnapshot);
        Assert.True(second.IsFromSnapshot);
        Assert.Equal(first.FileCount, second.FileCount);
        Assert.Equal("A", File.ReadAllText(second.Files["a.txt"].FullPath));
    }

    [Fact]
    public void SnapshotIsInvalidatedWhenAFolderContentChanges()
    {
        using var tree = new TestTree("scan-snapshot-invalidate");
        tree.Add("a.txt", "A");

        var store = new SnapshotStore(SyncFixture.NewTempDirectory("state"));
        var scanner = new FileScanner(new ExcludeMatcher(), snapshotStore: store);

        _ = scanner.Scan(tree.Root, "TESTSERIAL");
        tree.Add("b.txt", "B");

        ScanResult second = scanner.Scan(tree.Root, "TESTSERIAL");

        Assert.False(second.IsFromSnapshot);
        Assert.Equal(2, second.FileCount);
    }
}
