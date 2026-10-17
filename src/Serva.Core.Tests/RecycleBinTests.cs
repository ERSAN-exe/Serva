using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>系统回收站：删除后文件不再位于原位置（回收站里的副本由系统管理）。</summary>
public class RecycleBinTests
{
    [Fact]
    public void TryDelete_MovesFileOutOfOriginalLocation()
    {
        using var tree = new TestTree("recycle");
        string file = tree.Add("extra.txt", "EXTRA");

        Assert.True(RecycleBin.TryDelete(file));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void TryDelete_MissingPathReturnsFalse()
    {
        using var tree = new TestTree("recycle-missing");

        Assert.False(RecycleBin.TryDelete(tree.PathOf("nope.txt")));
        Assert.False(RecycleBin.TryDelete(string.Empty));
    }
}
