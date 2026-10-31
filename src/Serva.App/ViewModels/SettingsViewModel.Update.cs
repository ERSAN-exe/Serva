using Microsoft.UI.Xaml;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「设置」页 —— 版本更新（partial）。</summary>
public sealed partial class SettingsViewModel
{
    private readonly UpdateService _updates = UpdateService.Current;

    /// <summary>启动时是否检查更新（改动立即保存）。</summary>
    public bool CheckUpdatesOnStartup
    {
        get => _settings.Value.CheckUpdatesOnStartup;
        set
        {
            if (_settings.Value.CheckUpdatesOnStartup == value)
            {
                return;
            }

            _settings.Value.CheckUpdatesOnStartup = value;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>「当前版本 0.3.0」。</summary>
    public string CurrentVersionText =>
        AppText.Current.F("Current version {0}", "当前版本 {0}", AppVersion.Current);

    /// <summary>检查期间「检查更新」按钮置灰。</summary>
    public bool CanCheckUpdate => !_updates.IsChecking;

    /// <summary>检查结果 / 进度的一行状态文字。</summary>
    public string UpdateStatusText => _updates.StatusText;

    public Visibility UpdateStatusVisibility =>
        _updates.HasStatus ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>「0.2.0 · 查看更新」蓝链的文字。</summary>
    public string UpdateLinkText => _updates.LatestVersion is { Length: > 0 } version
        ? AppText.Current.F("{0} · View release", "{0} · 查看更新", version)
        : string.Empty;

    public Visibility UpdateLinkVisibility =>
        _updates.HasUpdate ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>「查看更新」的目标：GitHub 上最新的 Release 页面。</summary>
    public Uri UpdateUri => UpdateService.ReleasePageUri;

    /// <summary>更新状态变化后刷新本页绑定（由页面在 <c>Loaded</c> 期间订阅、<c>Unloaded</c> 时退订）。</summary>
    public void RefreshUpdateState()
    {
        OnPropertyChanged(nameof(CanCheckUpdate));
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(UpdateStatusVisibility));
        OnPropertyChanged(nameof(UpdateLinkText));
        OnPropertyChanged(nameof(UpdateLinkVisibility));
        OnPropertyChanged(nameof(UpdateUri));
    }
}
