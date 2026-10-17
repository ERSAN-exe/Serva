using Serva.Core.Models;

namespace Serva.Core.Tests;

/// <summary>构造比较 / 计划测试数据用的共享工具。</summary>
internal static class CompareFixture
{
    public static readonly DateTime Base = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    public static FileEntry Entry(string relativePath, long length, DateTime timeUtc) =>
        new(@"C:\src\" + relativePath.Replace('/', '\\'), relativePath, length, timeUtc, FileAttributes.Normal);

    /// <summary>本机侧条目（默认比设备侧新一天）。</summary>
    public static FileEntry Left(string path, long length = 10) => Entry(path, length, Base.AddDays(1));

    /// <summary>设备侧条目。</summary>
    public static FileEntry Right(string path, long length = 10) => Entry(path, length, Base);

    public static CompareResult BuildCompare(
        params (string Path, FileEntry? Left, FileEntry? Right, CompareStatus Status)[] rows)
    {
        var leftFiles = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        var rightFiles = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);
        var items = new List<CompareItem>();

        foreach ((string path, FileEntry? left, FileEntry? right, CompareStatus status) in rows)
        {
            if (left is not null)
            {
                leftFiles[path] = left;
            }

            if (right is not null)
            {
                rightFiles[path] = right;
            }

            items.Add(new CompareItem(path, left, right, status));
        }

        return new CompareResult(
            new ScanResult(@"C:\src", leftFiles, [], TimeSpan.Zero, DateTime.UtcNow),
            new ScanResult(@"E:\", rightFiles, [], TimeSpan.Zero, DateTime.UtcNow),
            items,
            TimeSpan.Zero);
    }
}
