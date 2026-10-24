namespace Serva.Core.Services;

/// <summary>
/// 本机标识：第一次运行时在 <c>%LocalAppData%\Serva\computer-id</c> 里生成一个 GUID 并长期复用。
/// <para>
/// 移动设备上的对应关系文件按「电脑」分段保存，用它区分是哪台电脑的配置 ——
/// 这样同一个 U 盘在不同电脑之间来回插，各自的对应关系都不会被对方覆盖。
/// 改机器名 / 改用户名都不影响它。
/// </para>
/// </summary>
public static class ComputerIdentity
{
    private static readonly Lazy<string> Id = new(LoadOrCreate, isThreadSafe: true);

    /// <summary>本机的稳定标识。</summary>
    public static string CurrentId => Id.Value;

    /// <summary>本机计算机名（只用于显示 / 人工排查）。</summary>
    public static string CurrentName => Environment.MachineName;

    private static string LoadOrCreate()
    {
        string path = Path.Combine(StorageLocations.RootDirectory, "computer-id");

        try
        {
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (existing.Length > 0)
                {
                    return existing;
                }
            }

            string created = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(StorageLocations.RootDirectory);
            File.WriteAllText(path, created);
            return created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进本机目录：退回「机器名 + 用户名」，同机稳定、跨机也不会串。
            return $"{Environment.MachineName}-{Environment.UserName}";
        }
    }
}