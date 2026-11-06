using Serva.Core.Models;

namespace Serva.Core.Tests;

/// <summary>执行策略（含旧版「包含规则」的迁移）。</summary>
public class SyncOptionsTests
{
    [Fact]
    public void Normalize_MovesLegacyIncludePatternsIntoAlwaysIncluded()
    {
        // 旧版本把设置页「包含规则」的规则写进了 IncludePatterns（白名单：没命中的文件全部消失）。
        // 现在统一搬进 AlwaysIncludePatterns（额外包含），用户加的规则照旧生效，但不会再滤掉别的文件。
        var options = new SyncOptions
        {
            IncludePatterns = ["/.minecraft/"],
            AlwaysIncludePatterns = [],
        };

        options.Normalize();

        Assert.Empty(options.IncludePatterns);
        Assert.Equal(["/.minecraft/"], options.AlwaysIncludePatterns);
    }

    [Fact]
    public void Normalize_DoesNotDuplicateIncludedRules()
    {
        var options = new SyncOptions
        {
            IncludePatterns = [".minecraft"],
            AlwaysIncludePatterns = [".minecraft"],
        };

        options.Normalize();

        Assert.Equal([".minecraft"], options.AlwaysIncludePatterns);
    }

    [Fact]
    public void Normalize_KeepsIncludedRulesEmptyByDefault()
    {
        var options = new SyncOptions();

        options.Normalize();

        // 默认不再预置 .minecraft：包含规则由用户自己添加。
        Assert.Empty(options.AlwaysIncludePatterns);
        Assert.Empty(options.IncludePatterns);
    }
}
