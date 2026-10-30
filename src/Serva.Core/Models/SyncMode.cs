namespace Serva.Core.Models;

/// <summary>同步模式（对应样稿「同步模式」的四选一）。</summary>
public enum SyncMode
{
    /// <summary>备份 → 设备：以本机为源，单向写入设备。</summary>
    BackupToDevice,

    /// <summary>设备 → 本机：以设备为源，单向写入本机。</summary>
    DeviceToLocal,

    /// <summary>双向同步（合并）：两侧较新的文件互相补充；两边都改过的文件保留较新版本。</summary>
    TwoWay,

    /// <summary>仅比较：只生成计划，不写入任何数据。</summary>
    CompareOnly,
}

/// <summary>单个文件将要执行的动作。</summary>
public enum PlanAction
{
    /// <summary>不执行（相同 / 仅比较模式）。</summary>
    None,

    /// <summary>本机 → 设备。</summary>
    CopyToDevice,

    /// <summary>设备 → 本机。</summary>
    CopyToLocal,

    /// <summary>删除设备端多余文件（移入系统回收站）。</summary>
    DeleteOnDevice,

    /// <summary>删除本机端多余文件（移入系统回收站）。</summary>
    DeleteOnLocal,

    /// <summary>冲突：等待用户决定。</summary>
    Conflict,
}
