using Serva.Core.Services;

namespace Serva.Core.Tests;

public class ExcludeMatcherTests
{
    [Fact]
    public void DefaultRules_ExcludeSystemNoise()
    {
        var matcher = new ExcludeMatcher();

        Assert.True(matcher.IsExcluded("Thumbs.db"));
        Assert.True(matcher.IsExcluded("a/b/Thumbs.db"));
        Assert.True(matcher.IsExcluded("desktop.ini"));
        Assert.True(matcher.IsExcluded("System Volume Information"));
        Assert.True(matcher.IsExcluded("_ServaTrash/2026/main.cs"));
        Assert.True(matcher.IsExcluded("._resource"));
        Assert.True(matcher.IsExcluded("dir/half.serva-part"));
        Assert.False(matcher.IsExcluded("docs/readme.md"));
        Assert.False(matcher.IsExcluded("src/Serva/Program.cs"));
    }

    [Theory]
    [InlineData("**/node_modules/**", "a/b/node_modules/c/d.js", true)]
    [InlineData("**/node_modules/**", "node_modules/d.js", true)]
    [InlineData("**/node_modules/**", "a/b/c.js", false)]
    [InlineData("*.tmp", "a/b.tmp", true)]
    [InlineData("*.tmp", "a/b.txt", false)]
    [InlineData("build/**", "build/x/y.dll", true)]
    [InlineData("build/**", "src/build/x.dll", false)]
    [InlineData("src/**/generated/*.cs", "src/a/b/generated/x.cs", true)]
    public void CustomPatterns_MatchAsExpected(string pattern, string path, bool expected)
    {
        var matcher = new ExcludeMatcher([pattern]);

        Assert.Equal(expected, matcher.IsExcluded(path));
    }

    [Theory]
    [InlineData("**/*.jpg", "a/b/c.jpg", false)]
    [InlineData("**/*.jpg", "a/b/c.png", true)]
    public void IncludePatterns_ActAsAllowList(string pattern, string path, bool expectedExcluded)
    {
        var matcher = new ExcludeMatcher([], [pattern]);

        Assert.Equal(expectedExcluded, matcher.IsExcluded(path));
    }

    [Fact]
    public void IncludeFolderPattern_CoversItsSubtree()
    {
        // 「包含某个文件夹」= 连同它下面的内容一起包含（嵌套路径同样适用）。
        var matcher = new ExcludeMatcher([], ["sub/hidden"]);

        Assert.False(matcher.IsExcluded("sub/hidden"));
        Assert.False(matcher.IsExcluded("sub/hidden/notes.txt"));
        Assert.True(matcher.IsExcluded("sub/other/notes.txt"));
    }

    [Fact]
    public void ExcludeFolderPattern_CoversItsSubtree()
    {
        var matcher = new ExcludeMatcher(["sub/cache"]);

        Assert.True(matcher.IsExcluded("sub/cache"));
        Assert.True(matcher.IsExcluded("sub/cache/x.bin"));
        Assert.False(matcher.IsExcluded("sub/other/x.bin"));
    }

    [Fact]
    public void ShouldDescend_KeepsAncestorsOfIncludeRules()
    {
        // 规则的上级目录必须继续下探，否则「包含 sub/hidden」永远走不到 hidden。
        var matcher = new ExcludeMatcher([], ["sub/hidden"]);

        Assert.True(matcher.ShouldDescend("sub"));
        Assert.True(matcher.ShouldDescend("sub/hidden"));
        Assert.False(matcher.ShouldDescend("other"));
    }

    [Fact]
    public void ShouldDescend_FollowsNameOnlyIncludeRules()
    {
        // 只写名字的「包含」规则（如隐藏文件夹名）可能出现在任意层级：必须一路下探去找。
        var matcher = new ExcludeMatcher([], ["HiddenDir"]);

        Assert.True(matcher.ShouldDescend("sub"));
        Assert.True(matcher.ShouldDescend("sub/deeper"));
        Assert.True(matcher.MayContainIncluded("sub/deeper"));
    }

    [Fact]
    public void ShouldDescend_StillStopsAtExcludeRules()
    {
        var matcher = new ExcludeMatcher(["**/node_modules/**"], ["*.log"]);

        Assert.False(matcher.ShouldDescend("a/node_modules"));
        Assert.True(matcher.ShouldDescend("a/src"));
    }

    [Fact]
    public void MayContainIncluded_CoversExplicitFoldersAndTheirAncestors()
    {
        var matcher = new ExcludeMatcher([], ["sub/hidden"]);

        Assert.True(matcher.MayContainIncluded("sub"));
        Assert.True(matcher.MayContainIncluded("sub/hidden"));
        Assert.True(matcher.MayContainIncluded("sub/hidden/deeper"));
        Assert.False(matcher.MayContainIncluded("other"));
    }

    [Fact]
    public void NormalizesDotSlashPrefix()
    {
        // 用户按资源管理器的习惯写成「.\隐藏文件夹」：等于「隐藏文件夹」。
        var matcher = new ExcludeMatcher([], [@".\Hidden"]);

        Assert.False(matcher.IsExcluded("Hidden/notes.txt"));
        Assert.True(matcher.ShouldDescend("sub"));
    }

    [Fact]
    public void NormalizesBackslashesAndCase()
    {
        var matcher = new ExcludeMatcher(["A/B"]);

        Assert.True(matcher.IsExcluded(@"a\b"));
        Assert.True(matcher.IsExcluded("a/B"));
    }

    [Fact]
    public void AlwaysIncludePatterns_OverrideExcludeRules()
    {
        // 「排除所有点开头的目录」，但 .minecraft 在「包含规则」里 —— 以包含为准。
        var matcher = new ExcludeMatcher([".*"], [], [".minecraft"]);

        Assert.False(matcher.IsExcluded(".minecraft"));
        Assert.False(matcher.IsExcluded(".minecraft/saves/world.dat"));
        Assert.True(matcher.IsExcluded(".git/config"));
    }

    [Fact]
    public void AlwaysIncludePatterns_AreEmptyByDefault()
    {
        // 默认不再预置 .minecraft：需要时由用户在「设置 → 执行规则 → 包含规则」里自己添加。
        Assert.False(new ExcludeMatcher().IsExplicitlyIncluded(".minecraft"));
        Assert.False(new ExcludeMatcher([], [], []).IsExplicitlyIncluded(".minecraft"));
    }

    [Fact]
    public void EmptyPathIsNeverExcluded()
    {
        var matcher = new ExcludeMatcher();

        Assert.False(matcher.IsExcluded(string.Empty));
        Assert.False(matcher.IsExcluded("   "));
    }
}
