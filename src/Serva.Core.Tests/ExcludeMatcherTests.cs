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
    public void NormalizesBackslashesAndCase()
    {
        var matcher = new ExcludeMatcher(["A/B"]);

        Assert.True(matcher.IsExcluded(@"a\b"));
        Assert.True(matcher.IsExcluded("a/B"));
    }

    [Fact]
    public void AlwaysIncludePatterns_OverrideExcludeRules()
    {
        // 「排除所有点开头的目录」，但 .minecraft 在「包含的文件」里 —— 以包含为准。
        var matcher = new ExcludeMatcher([".*"], [], [".minecraft"]);

        Assert.False(matcher.IsExcluded(".minecraft"));
        Assert.False(matcher.IsExcluded(".minecraft/saves/world.dat"));
        Assert.True(matcher.IsExcluded(".git/config"));
    }

    [Fact]
    public void AlwaysIncludePatterns_CanBeCleared()
    {
        // 设置页把 .minecraft 删掉之后：内置的「隐藏也照扫」也随之失效。
        var matcher = new ExcludeMatcher([], [], []);

        Assert.False(matcher.IsExplicitlyIncluded(".minecraft"));
        Assert.True(new ExcludeMatcher().IsExplicitlyIncluded(".minecraft"));
    }

    [Fact]
    public void EmptyPathIsNeverExcluded()
    {
        var matcher = new ExcludeMatcher();

        Assert.False(matcher.IsExcluded(string.Empty));
        Assert.False(matcher.IsExcluded("   "));
    }
}
