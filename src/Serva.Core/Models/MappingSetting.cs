namespace Serva.Core.Models;

/// <summary>
/// 一条对应关系：本机文件夹 ↔ 移动设备上的文件夹。
/// <para>同步时以「本机文件夹」为根的相对路径，会映射到「设备文件夹」下的同名相对路径。</para>
/// </summary>
public sealed class MappingSetting
{
    /// <summary>本机来源文件夹的绝对路径。</summary>
    public string LocalRoot { get; set; } = string.Empty;

    /// <summary>设备上的文件夹（相对设备根目录；空字符串表示设备根目录）。</summary>
    public string DeviceFolder { get; set; } = string.Empty;

    public MappingSetting Clone() => (MappingSetting)MemberwiseClone();
}
