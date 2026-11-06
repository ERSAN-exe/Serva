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
    public void SkipsHiddenFilesAndHiddenDirectories()
    {
        using var tree = new TestTree("scan-hidden");
        tree.Add("keep.txt", "K");
        tree.Add("visible/inside.txt", "I");

        string hiddenFile = tree.Add("hidden.txt", "H");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        // 整个目录隐藏（.git / 应用缓存目录之类）：里面的文件自身没有隐藏属性，也必须一起跳过。
        tree.Add(".git/config", "G");
        File.SetAttributes(Path.Combine(tree.Root, ".git"), FileAttributes.Hidden);

        // 点开头的目录在 exFAT / FAT 上可能没有隐藏属性：按名字同样跳过。
        tree.Add(".vscode/settings.json", "{}");

        ScanResult result = new FileScanner(new ExcludeMatcher(), skipHiddenAndSystem: true).Scan(tree.Root);

        Assert.Equal(
            ["keep.txt", "visible/inside.txt"],
            result.Files.Keys.OrderBy(static k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void HiddenEntriesAreScanned_WhenTheFlagIsOff()
    {
        using var tree = new TestTree("scan-hidden-off");
        string hiddenFile = tree.Add("hidden.txt", "H");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);

        ScanResult result = new FileScanner(new ExcludeMatcher(), skipHiddenAndSystem: false).Scan(tree.Root);

        Assert.True(result.Files.ContainsKey("hidden.txt"));
    }

    [Fact]
    public void ExplicitlyIncludedHiddenFolder_IsScanned()
    {
        // 用户把隐藏文件夹加成「仅包含」：说明他就要这个文件夹，不能因为隐藏而跳过。
        using var tree = new TestTree("scan-hidden-include");
        tree.Add(".git/config", "G");
        tree.Add(".git/HEAD", "H");
        File.SetAttributes(Path.Combine(tree.Root, ".git"), FileAttributes.Hidden);

        var matcher = new ExcludeMatcher(includePatterns: [".git/**"]);
        ScanResult result = new FileScanner(matcher, skipHiddenAndSystem: true).Scan(tree.Root);

        Assert.Equal(2, result.FileCount);
        Assert.Contains(".git/config", result.Files.Keys);
    }

    [Fact]
    public void ExplicitlyIncludedHiddenFolder_IncludesHiddenEntriesInside()
    {
        // 隐藏文件夹里的内容也可能带隐藏属性：文件夹被点名包含，里面的东西就要一起算。
        using var tree = new TestTree("scan-hidden-inside");
        tree.Add("HiddenDir/a.txt", "A");
        string hiddenFile = tree.Add("HiddenDir/hidden.txt", "H");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);
        File.SetAttributes(tree.PathOf("HiddenDir"), FileAttributes.Hidden);

        var matcher = new ExcludeMatcher(includePatterns: ["HiddenDir"]);
        ScanResult result = new FileScanner(matcher, skipHiddenAndSystem: true).Scan(tree.Root);

        Assert.Equal(
            ["HiddenDir/a.txt", "HiddenDir/hidden.txt"],
            result.Files.Keys.OrderBy(static k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void IncludeRuleForNestedFolder_ReachesTheFolder()
    {
        // 「包含 sub/HiddenDir」：上级 sub 本身不命中白名单，也必须继续下探，否则永远走不到目标。
        using var tree = new TestTree("scan-nested-include");
        tree.Add("sub/HiddenDir/c.txt", "C");
        tree.Add("sub/other/d.txt", "D");
        File.SetAttributes(tree.PathOf("sub/HiddenDir"), FileAttributes.Hidden);

        var matcher = new ExcludeMatcher(includePatterns: ["sub/HiddenDir"]);
        ScanResult result = new FileScanner(matcher, skipHiddenAndSystem: true).Scan(tree.Root);

        Assert.Equal(["sub/HiddenDir/c.txt"], result.Files.Keys.OrderBy(static k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void IncludeRuleForFolderInsideHiddenFolder_ReachesTheFolder()
    {
        // 隐藏目录里面被点名的文件夹：隐藏目录要进去（只当通道），它自己不算内容。
        using var tree = new TestTree("scan-hidden-parent-include");
        tree.Add("HiddenParent/child/d.txt", "D");
        tree.Add("HiddenParent/other/e.txt", "E");
        File.SetAttributes(tree.PathOf("HiddenParent"), FileAttributes.Hidden);

        var matcher = new ExcludeMatcher(includePatterns: ["HiddenParent/child"]);
        ScanResult result = new FileScanner(matcher, skipHiddenAndSystem: true).Scan(tree.Root);

        Assert.Equal(
            ["HiddenParent/child/d.txt"],
            result.Files.Keys.OrderBy(static k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void IncludeRuleByName_FindsHiddenFolderAtAnyDepth()
    {
        // 用户只写名字把隐藏文件夹加进「包含」：它在子目录里也要能找到（不能因为上级没命中白名单被剪掉）。
        using var tree = new TestTree("scan-include-by-name");
        tree.Add("sub/deeper/HiddenDir/notes.txt", "N");
        tree.Add("sub/other/x.txt", "X");
        File.SetAttributes(tree.PathOf("sub/deeper/HiddenDir"), FileAttributes.Hidden);

        var matcher = new ExcludeMatcher(includePatterns: ["HiddenDir"]);
        ScanResult result = new FileScanner(matcher, skipHiddenAndSystem: true).Scan(tree.Root);

        Assert.Equal(
            ["sub/deeper/HiddenDir/notes.txt"],
            result.Files.Keys.OrderBy(static k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void IncludedRuleName_DoesNotHideOtherFiles()
    {
        // 「包含规则」是额外包含：加了 .minecraft 之后，别的文件照旧参与同步
        // （以前写进 IncludePatterns 白名单，会让所有没命中的文件消失）。
        using var tree = new TestTree("scan-include-additive");
        tree.Add("1.txt", "1");
        tree.Add("2.txt", "2");
        tree.Add(".minecraft/saves/world.dat", "W");

        var matcher = new ExcludeMatcher([], [], ["/.minecraft/"]);
        ScanResult result = new FileScanner(matcher, skipHiddenAndSystem: true).Scan(tree.Root);

        Assert.Equal(
            [".minecraft/saves/world.dat", "1.txt", "2.txt"],
            result.Files.Keys.OrderBy(static k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void MinecraftFolder_IsSkippedByDefault()
    {
        // .minecraft 以点开头（算隐藏）：默认的「包含规则」是空的，所以默认不会同步它 ——
        // 需要时在「设置 → 执行规则 → 包含规则」里加一条即可（见 IncludedRuleName_DoesNotHideOtherFiles）。
        using var tree = new TestTree("scan-minecraft");
        tree.Add(".minecraft/saves/world.dat", "W");
        tree.Add("visible.txt", "V");

        ScanResult result = new FileScanner(new ExcludeMatcher(), skipHiddenAndSystem: true).Scan(tree.Root);

        Assert.Single(result.Files);
        Assert.Contains("visible.txt", result.Files.Keys);
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
    public void RescanAlwaysReadsFreshFileTimes()
    {
        // 修改文件内容不会改变所在目录的时间戳：以前用「目录时间戳 + 快照」跳过读盘，
        // 复扫会拿到旧的修改时间，导致「设备端文件更新却显示本机较新」。现在每次扫描都读真实元数据。
        using var tree = new TestTree("scan-fresh");
        string file = tree.Add("a.txt", "OLD");

        ScanResult first = new FileScanner(new ExcludeMatcher()).Scan(tree.Root, "TESTSERIAL");
        Assert.False(first.IsFromSnapshot);

        DateTime updated = CompareFixture.Base.AddDays(3);
        File.WriteAllText(file, "NEW CONTENT");
        File.SetLastWriteTimeUtc(file, updated);

        ScanResult second = new FileScanner(new ExcludeMatcher()).Scan(tree.Root, "TESTSERIAL");

        Assert.False(second.IsFromSnapshot);
        Assert.Equal(updated, second.Files["a.txt"].LastWriteTimeUtc);
        Assert.Equal("NEW CONTENT".Length, second.Files["a.txt"].Length);
    }

    [Fact]
    public void RescanSeesFilesAddedAfterTheFirstScan()
    {
        using var tree = new TestTree("scan-snapshot-invalidate");
        tree.Add("a.txt", "A");

        var scanner = new FileScanner(new ExcludeMatcher());
        _ = scanner.Scan(tree.Root, "TESTSERIAL");
        tree.Add("b.txt", "B");

        ScanResult second = scanner.Scan(tree.Root, "TESTSERIAL");

        Assert.False(second.IsFromSnapshot);
        Assert.Equal(2, second.FileCount);
    }
}
