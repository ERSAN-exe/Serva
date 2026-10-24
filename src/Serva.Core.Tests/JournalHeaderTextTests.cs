using Serva.Core.Localization;
using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>日志抬头（Move 条目）的说明要按「当前语言」现算 —— 否则切换语言后会出现中英混排。</summary>
public class JournalHeaderTextTests
{
    [Fact]
    public void RunHeader_FollowsCurrentLanguage()
    {
        Func<string, string, string> original = CoreText.Resolver;

        try
        {
            var entry = new JournalEntry(
                DateTime.UtcNow,
                JournalOperationKind.Move,
                string.Empty,
                @"C:\Users\me\Desktop\Pictures",
                @"G:\Pictures",
                0,
                true,
                RunHeaderCode.Merge);

            CoreText.Resolver = static (_, chinese) => chinese;
            Assert.Equal("由 C: 移动到 G:（合并）", entry.DetailText);

            CoreText.Resolver = static (english, _) => english;
            Assert.Equal("Moved from C: to G: (merge)", entry.DetailText);
        }
        finally
        {
            CoreText.Resolver = original;
        }
    }

    [Fact]
    public void OverwriteHeader_DescribesDirection()
    {
        var entry = new JournalEntry(
            DateTime.UtcNow,
            JournalOperationKind.Move,
            string.Empty,
            @"C:\a",
            @"G:\b",
            0,
            true,
            RunHeaderCode.OverwriteDeviceToLocal);

        Assert.Equal("Overwrote C: with the contents of G: (source G:)", entry.DetailText);
    }

    [Fact]
    public void LegacyRunHeader_AlsoFollowsCurrentLanguage()
    {
        // 旧日志里 Message 是当时翻译好的文字（如「合并」）：认出来之后同样按当前语言现算。
        var entry = new JournalEntry(
            DateTime.UtcNow,
            JournalOperationKind.Move,
            "由 C: 移动到 G:（合并）",
            @"C:\a",
            @"G:\b",
            0,
            true,
            "合并");

        Assert.Equal("Moved from C: to G: (merge)", entry.DetailText);
    }

    [Fact]
    public void UnknownRunHeader_FallsBackToStoredSentence()
    {
        // 认不出来的旧写法：保持原样，不猜。
        var entry = new JournalEntry(
            DateTime.UtcNow,
            JournalOperationKind.Move,
            "以前记下的说明",
            @"C:\a",
            @"G:\b",
            0,
            true,
            "某种未知方式");

        Assert.Equal("以前记下的说明", entry.DetailText);
    }
}