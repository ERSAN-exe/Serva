using System.Reflection;

namespace Serva.Core.Services;

/// <summary>
/// 应用版本号（单一来源：<c>Serva.App.csproj</c> 里的 <c>&lt;Version&gt;</c>，
/// 编译后写进程序集的 <see cref="AssemblyInformationalVersionAttribute"/>）。
/// </summary>
public static class AppVersion
{
    /// <summary>当前入口程序集的版本，例如 <c>0.1.0</c>（已去掉 <c>+构建元数据</c>）。</summary>
    public static string Current { get; } = Read(Assembly.GetEntryAssembly());

    /// <summary>
    /// 读取指定程序集的版本；拿不到时返回 <c>0.0.0</c>。
    /// <para>单独暴露出来便于测试（单元测试的入口程序集是测试宿主，不是 Serva）。</para>
    /// </summary>
    public static string Read(Assembly? assembly)
    {
        if (assembly is null)
        {
            return "0.0.0";
        }

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // .NET 8+ 会自动追加源码版本："0.1.0+3f2a1c9" → "0.1.0"
            int plus = informational.IndexOf('+');
            return (plus > 0 ? informational[..plus] : informational).Trim();
        }

        Version? version = assembly.GetName().Version;

        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }
}
