namespace Serva.Core.Models;

/// <summary>
/// 两端文件的比较状态。较新的两种状态在结果表里显示为「该侧盘符 较新」（如「E: 较新」）。
/// </summary>
public enum CompareStatus
{
    /// <summary>相同：大小一致，且修改时间在容差范围内。</summary>
    Same,

    /// <summary>仅本机存在（设备侧缺失）。</summary>
    LeftOnly,

    /// <summary>仅设备存在（本机侧缺失）。</summary>
    RightOnly,

    /// <summary>本机侧较新。</summary>
    LeftNewer,

    /// <summary>设备侧较新。</summary>
    RightNewer,

    /// <summary>两端都改动过，无法自动判定哪一侧较新，需要人工处理。</summary>
    Conflict,

    /// <summary>镜像删除：目标端多余，将被移入回收文件夹。</summary>
    Delete,
}
