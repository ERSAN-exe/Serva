namespace Serva.Core.Tests;

/// <summary>在临时目录中构造一棵文件树，测试结束后自动清理。</summary>
internal sealed class TestTree : IDisposable
{
    public TestTree(string name)
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "serva-tests",
            $"{name}-{Guid.NewGuid():N}");

        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    /// <summary>写入一个文件，可指定修改时间（UTC）。</summary>
    public string Add(string relativePath, string content, DateTime? lastWriteUtc = null)
    {
        string full = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        if (lastWriteUtc is not null)
        {
            File.SetLastWriteTimeUtc(full, lastWriteUtc.Value);
        }

        return full;
    }

    public string PathOf(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public bool Exists(string relativePath) => File.Exists(PathOf(relativePath));

    public string Read(string relativePath) => File.ReadAllText(PathOf(relativePath));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
