namespace Serva.Core.Models;

/// <summary>持久化到 <c>%LocalAppData%\Serva\settings.json</c> 的应用设置。</summary>
public sealed class AppSettings
{
    /// <summary>明暗主题：<c>system</c> / <c>light</c> / <c>dark</c>。</summary>
    public string Theme { get; set; } = "system";

    /// <summary>
    /// 界面语言：<c>system</c>（跟随系统，默认）/ <c>en-US</c> / <c>zh-Hans</c>。
    /// </summary>
    public string Language { get; set; } = "system";

    /// <summary>
    /// 是否允许把本机硬盘（<strong>不含系统盘</strong>）也当作同步目标。
    /// 默认关闭：设备列表里只显示 U 盘 / 移动硬盘。
    /// </summary>
    public bool AllowLocalDrives { get; set; }

    /// <summary>执行策略与排除规则。</summary>
    public SyncOptions Options { get; set; } = new();

    /// <summary>「本机文件夹 ↔ 设备文件夹」对应关系（用于启动时恢复）。</summary>
    public List<MappingSetting> Mappings { get; set; } = [];

    /// <summary>上次使用的目标设备根路径。</summary>
    public string? LastTargetRoot { get; set; }

    /// <summary>
    /// 上次使用的目标设备身份（硬件 ID + 卷序列号）。用于在设备换了盘符、或别的设备占用
    /// 同一盘符时，仍能准确判断「是不是同一台设备」。
    /// </summary>
    public string? LastTargetIdentity { get; set; }

    /// <summary>启动时是否检查 GitHub 上的新版本（默认开启）。</summary>
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>上次检查更新的时间（UTC），用于几分钟内的重复启动去重。</summary>
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// 上次检查时远程发布的最新版本号。
    /// <para>
    /// 启动时先用它在本地恢复「发现新版本」的状态，打开软件就能看到提示条 / 导航小红点，
    /// 不必等这一轮联网检查（断网时也能提示上次查到的结果）。
    /// </para>
    /// </summary>
    public string? LatestKnownVersion { get; set; }

    /// <summary>用户已经忽略过的新版本号，避免每次启动重复提醒。</summary>
    public string? DismissedUpdateVersion { get; set; }

    /// <summary>
    /// 固定（收藏）的本机文件夹：会作为快捷入口出现在「选择本机文件夹」对话框里。
    /// </summary>
    public List<string> PinnedFolders { get; set; } = [];

    public AppSettings Clone() => new()
    {
        Theme = Theme,
        Language = Language,
        AllowLocalDrives = AllowLocalDrives,
        Options = Options.Clone(),
        Mappings = [.. Mappings.Select(static m => m.Clone())],
        LastTargetRoot = LastTargetRoot,
        LastTargetIdentity = LastTargetIdentity,
        CheckUpdatesOnStartup = CheckUpdatesOnStartup,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
        LatestKnownVersion = LatestKnownVersion,
        DismissedUpdateVersion = DismissedUpdateVersion,
        PinnedFolders = [.. PinnedFolders],
    };
}
