using Serva.Core.Services;

namespace Serva.Core.Tests;

/// <summary>
/// 不允许把系统盘根目录与系统文件夹当作同步来源（镜像删除会把系统当成「多余文件」删掉）。
/// </summary>
public class ProtectedPathsTests
{
    private static string SystemRoot => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string SystemDrive => Path.GetPathRoot(SystemRoot) ?? @"C:\";

    [Fact]
    public void SystemDriveRoot_IsProtected()
    {
        Assert.Equal(ProtectedPathKind.SystemDriveRoot, ProtectedPaths.Classify(SystemDrive));
        Assert.Equal(ProtectedPathKind.SystemDriveRoot, ProtectedPaths.Classify(SystemDrive.TrimEnd('\\')));
        Assert.True(ProtectedPaths.IsProtected(SystemDrive));
    }

    [Fact]
    public void WindowsAndProgramFolders_AreProtected_IncludingTheirSubfolders()
    {
        Assert.Equal(ProtectedPathKind.SystemFolder, ProtectedPaths.Classify(SystemRoot));
        Assert.Equal(ProtectedPathKind.SystemFolder, ProtectedPaths.Classify(Path.Combine(SystemRoot, "System32")));
        Assert.Equal(ProtectedPathKind.SystemFolder, ProtectedPaths.Classify(Path.Combine(SystemRoot, "System32", "drivers")));

        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            Assert.Equal(ProtectedPathKind.SystemFolder, ProtectedPaths.Classify(programFiles));
            Assert.Equal(ProtectedPathKind.SystemFolder, ProtectedPaths.Classify(Path.Combine(programFiles, "Serva", "bin")));
        }

        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(programData))
        {
            Assert.Equal(ProtectedPathKind.SystemFolder, ProtectedPaths.Classify(programData));
        }
    }

    [Fact]
    public void VolumeSystemDirectories_AreProtectedByName()
    {
        Assert.Equal(
            ProtectedPathKind.SystemFolder,
            ProtectedPaths.Classify(Path.Combine(SystemDrive, "System Volume Information")));

        Assert.Equal(
            ProtectedPathKind.SystemFolder,
            ProtectedPaths.Classify(Path.Combine(SystemDrive, "$RECYCLE.BIN")));
    }

    [Fact]
    public void SimilarName_IsNotTreatedAsSystemFolder()
    {
        // 「C:\Windows7」不是 Windows 目录：前缀比较必须带上分隔符。
        Assert.Equal(
            ProtectedPathKind.None,
            ProtectedPaths.Classify(Path.Combine(SystemDrive, "Windows7")));
    }

    [Fact]
    public void NormalUserFolder_IsAllowed()
    {
        // 用户目录下的普通文件夹可以选（注意不能用 %TEMP%：它在 AppData 里，属于受保护的系统目录）。
        string allowed = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "ServaTestAllowedFolder");

        Assert.Equal(ProtectedPathKind.None, ProtectedPaths.Classify(allowed));
        Assert.False(ProtectedPaths.IsProtected(allowed));
        Assert.Equal(ProtectedPathKind.None, ProtectedPaths.Classify(null));
        Assert.Equal(ProtectedPathKind.None, ProtectedPaths.Classify(string.Empty));
    }

    [Fact]
    public void AppDataIsProtected()
    {
        // 用户配置目录（含 Local / Roaming）是隐藏的系统目录，不能当同步来源。
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string appData = Path.Combine(userProfile, "AppData");

        Assert.Equal(ProtectedPathKind.SystemFolder, ProtectedPaths.Classify(appData));
        Assert.Equal(ProtectedPathKind.SystemFolder, ProtectedPaths.Classify(Path.Combine(appData, "Local", "Temp")));
    }
}
