using System.Runtime.InteropServices;

namespace Serva.Core.Services;

/// <summary>
/// 把文件 / 文件夹送进 Windows 系统回收站（可从回收站还原），而不是永久删除。
/// <para>
/// 用 shell 的 <c>SHFileOperation</c> 加 <c>FOF_ALLOWUNDO</c>：系统自己决定放到哪个盘的
/// <c>$Recycle.Bin</c>，无回收站的介质（部分小容量 U 盘）由系统按它的规则处理。
/// 全程静默（<c>FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI</c>），不弹系统对话框。
/// </para>
/// </summary>
public static class RecycleBin
{
    private const uint DeleteOperation = 0x0003;          // FO_DELETE
    private const ushort FlagSilent = 0x0004;             // FOF_SILENT
    private const ushort FlagNoConfirmation = 0x0010;     // FOF_NOCONFIRMATION
    private const ushort FlagAllowUndo = 0x0040;          // FOF_ALLOWUNDO：放进回收站
    private const ushort FlagNoErrorUi = 0x0400;          // FOF_NOERRORUI

    /// <summary>路径存在时把它放入系统回收站。</summary>
    /// <returns>成功交给系统处理返回 <c>true</c>；路径不存在或失败返回 <c>false</c>。</returns>
    public static bool TryDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !(File.Exists(path) || Directory.Exists(path)))
        {
            return false;
        }

        var operation = new FileOperation
        {
            Function = DeleteOperation,

            // 路径列表以两个 '\0' 结尾。
            From = path + '\0' + '\0',
            Flags = (ushort)(FlagAllowUndo | FlagNoConfirmation | FlagSilent | FlagNoErrorUi),
        };

        try
        {
            return SHFileOperation(ref operation) == 0 && !operation.AnyOperationsAborted;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref FileOperation operation);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileOperation
    {
        public nint WindowHandle;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool AnyOperationsAborted;

        public nint NameMappings;
        public string? ProgressTitle;
    }
}
