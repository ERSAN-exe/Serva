using Serva.Core.Services;

namespace Serva.Core.Models;

/// <summary>执行策略（样稿「设置 → 执行策略」）。M1–M4 阶段使用默认值。</summary>
public sealed class SyncOptions
{
    /// <summary>包含子目录。<strong>始终开启</strong>（界面不再提供开关）。</summary>
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>删除目标端多余文件（镜像删除）。默认关闭。</summary>
    public bool MirrorDelete { get; set; }

    /// <summary>
    /// 删除时放进 <strong>Windows 系统回收站</strong>（之后可从回收站还原）；
    /// 关闭则直接永久删除。界面文案：「删除时移入系统回收站」。
    /// </summary>
    public bool UseTrashFolder { get; set; } = true;

    /// <summary>复制后校验（比对源文件与目标文件的 SHA-256）。<strong>始终开启</strong>（界面不再提供开关）。</summary>
    public bool VerifyAfterCopy { get; set; } = true;

    /// <summary>
    /// 跳过隐藏 / 系统文件。<strong>始终开启</strong>（界面不再提供开关，见 <see cref="Normalize"/>）。
    /// </summary>
    public bool SkipHiddenAndSystem { get; set; } = true;

    /// <summary>
    /// 覆盖目标端已有文件前，先把原件挪进同目录的隐藏文件夹 <c>.ServaUndoTemp</c>，供撤销还原。
    /// <para>
    /// 关闭后覆盖就是「直接盖掉」，撤销时无法还原被覆盖的内容；软件退出时这些副本一律删除。
    /// </para>
    /// </summary>
    public bool KeepOverwriteBackup { get; set; } = true;

    /// <summary>
    /// 并发复制数：默认按 CPU 自动取值（读写充分并行，又不会吃满系统）。
    /// 界面不再提供该设置，需要时可显式赋值。
    /// </summary>
    public int Concurrency { get; set; } = DefaultConcurrency;

    /// <summary>
    /// 自动并发度：CPU 逻辑核数的一半，限制在 2–6。<br />
    /// 上限之所以压到 6 —— 移动存储是随机 I/O，并发过高反而更慢、更吵、更热。
    /// </summary>
    public static int DefaultConcurrency => Math.Clamp(Environment.ProcessorCount / 2, 2, 6);

    /// <summary>
    /// 两端都改动过时的处理方式。界面不再提供该设置：
    /// 「合并」按<strong>较新版本</strong>处理（谁新用谁覆盖），其余单向模式按「两者都保留」合并。
    /// </summary>
    public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.KeepBoth;

    /// <summary>排除规则（glob，支持 * 与 **）。默认含内置的文件夹 / 文件规则。</summary>
    public List<string> ExcludePatterns { get; set; } = [.. ExcludeMatcher.DefaultExcludePatterns];

    /// <summary>
    /// 旧版本的「仅包含」白名单：非空时，未命中的文件一律忽略。
    /// <para>
    /// 设置页的「包含的文件」其实是「额外包含」的意思，所以界面不再往这里写规则；旧设置里的条目
    /// 会在 <see cref="Normalize"/> 里搬进 <see cref="AlwaysIncludePatterns"/>。
    /// 保留这个属性只是为了读得懂旧设置文件（以及 <see cref="ExcludeMatcher"/> 的白名单能力）。
    /// </para>
    /// </summary>
    public List<string> IncludePatterns { get; set; } = [];

    /// <summary>
    /// 「包含规则」：命中的路径<strong>不会被排除</strong>（优先于排除规则），
    /// 并且即使带「隐藏 / 系统」属性也照常扫描。
    /// <para>
    /// 默认<strong>为空</strong>（不再预置 <c>.minecraft</c>）。用户可以在「设置 → 执行规则 → 包含规则」
    /// 里增删，删掉后不会自动补回。
    /// </para>
    /// </summary>
    public List<string> AlwaysIncludePatterns { get; set; } = [.. ExcludeMatcher.DefaultIncludedPatterns];

    /// <summary>设备被拔出时立即中止执行（已完成的部分保留）。</summary>
    public bool AbortOnDeviceDisconnect { get; set; } = true;

    /// <summary>
    /// 把界面已不再提供的选项归一到固定策略：
    /// 并发数按 CPU 自动取值（2–6），冲突固定「两者都保留」（即弹窗里的「合并」），
    /// <strong>始终包含子目录、始终复制后校验</strong>（这两个开关已从设置页移除）。
    /// <para>同时补上 Serva 自身的排除规则 —— 旧设置文件里没有这些条目。</para>
    /// </summary>
    public void Normalize()
    {
        Concurrency = DefaultConcurrency;
        ConflictResolution = ConflictResolution.KeepBoth;

        // 设置页已移除这两个开关：旧设置文件里的 false 也要纠正过来。
        IncludeSubdirectories = true;
        VerifyAfterCopy = true;

        // 「跳过隐藏和系统文件」同样改为始终开启（界面上已经没有这个开关）。
        SkipHiddenAndSystem = true;

        // 内置排除规则一律不可删除：旧设置文件里缺的条目在这里补回来
        // （对应关系文件、覆盖前副本目录也在默认规则里，所以下面两块不用再单独处理）。
        foreach (string rule in ExcludeMatcher.DefaultExcludePatterns)
        {
            if (!ExcludePatterns.Contains(rule, StringComparer.OrdinalIgnoreCase))
            {
                ExcludePatterns.Add(rule);
            }
        }

        // 「包含规则」不在这里补默认值（默认就是空的）：用户删掉的规则不该再被自动加回来。
        // 旧设置文件里没有这一项时，反序列化会保留属性初始化器给出的默认值。

        // 旧版本把「包含规则」写进了 IncludePatterns（白名单：没命中的文件全部消失），
        // 与设置页上「包含」的含义（额外包含、隐藏也照扫）相反。这里统一搬进 AlwaysIncludePatterns，
        // 用户添加过的规则照旧生效，但不会再让别的文件消失。
        if (IncludePatterns.Count > 0)
        {
            foreach (string rule in IncludePatterns)
            {
                if (!string.IsNullOrWhiteSpace(rule)
                    && !AlwaysIncludePatterns.Contains(rule, StringComparer.OrdinalIgnoreCase))
                {
                    AlwaysIncludePatterns.Add(rule);
                }
            }

            IncludePatterns.Clear();
        }
    }

    public SyncOptions Clone() => new()
    {
        IncludeSubdirectories = IncludeSubdirectories,
        MirrorDelete = MirrorDelete,
        UseTrashFolder = UseTrashFolder,
        VerifyAfterCopy = VerifyAfterCopy,
        SkipHiddenAndSystem = SkipHiddenAndSystem,
        KeepOverwriteBackup = KeepOverwriteBackup,
        Concurrency = Concurrency,
        ConflictResolution = ConflictResolution,
        ExcludePatterns = [.. ExcludePatterns],
        IncludePatterns = [.. IncludePatterns],
        AlwaysIncludePatterns = [.. AlwaysIncludePatterns],
        AbortOnDeviceDisconnect = AbortOnDeviceDisconnect,
    };
}

/// <summary>冲突处理方式（对应样稿的四个单选）。</summary>
public enum ConflictResolution
{
    /// <summary>保留本机版本（覆盖设备）。</summary>
    KeepLeft,

    /// <summary>保留设备版本（覆盖本机）。</summary>
    KeepRight,

    /// <summary>两者都保留（目标端重命名为「名称（设备版本）.ext」）。</summary>
    KeepBoth,

    /// <summary>跳过该文件，保留现状。</summary>
    Skip,
}
