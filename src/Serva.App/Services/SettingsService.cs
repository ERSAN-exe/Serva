using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.Services;

/// <summary>
/// 应用级共享设置：所有页面共用<strong>同一份</strong> <see cref="AppSettings"/> 实例，
/// 任何页面保存后其他页面都会通过 <see cref="Changed"/> 得到通知。
/// </summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store = new();

    private SettingsService()
    {
        Value = _store.Load();
        Value.Options.Normalize();   // 界面不再提供的选项（并发数 / 冲突策略）统一归一到固定策略
    }

    public static SettingsService Current { get; } = new();

    public AppSettings Value { get; private set; }

    /// <summary>设置已发生变化（已保存）。</summary>
    public event EventHandler? Changed;

    public string SettingsPath => _store.SettingsPath;

    /// <summary>保存当前设置并广播通知。</summary>
    public void Save()
    {
        _store.Save(Value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 保存设置但<strong>不广播</strong> <see cref="Changed"/>。
    /// <para>
    /// 用于后台维护的字段（例如「上次检查更新的时间」）：这类写入不该触发
    /// 界面上的重新扫描 / 重新生成计划。
    /// </para>
    /// </summary>
    public void SaveQuietly() => _store.Save(Value);

    /// <summary>丢弃内存中的修改，重新从磁盘读取。</summary>
    public void Reload()
    {
        Value = _store.Load();
        Value.Options.Normalize();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>恢复默认设置（删除设置文件）。</summary>
    public void ResetToDefaults()
    {
        Value = _store.Reset();
        Value.Options.Normalize();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Export(string destinationPath) => _store.Export(destinationPath, Value);

    public void Import(string sourcePath)
    {
        Value = _store.Import(sourcePath);
        Value.Options.Normalize();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
