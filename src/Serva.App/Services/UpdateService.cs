using System.ComponentModel;
using Serva.App.Localization;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.Services;

/// <summary>
/// 版本更新提醒。
/// <para>
/// 启动后在后台检查一次（默认 24 小时内不重复），发现更新的正式版本时通过
/// <see cref="Changed"/> 通知界面：比较页顶部的提示条、「设置」页的更新卡片、导航项上的小红点。
/// </para>
/// <para>
/// 「查看更新」统一打开 <c>https://github.com/ERSAN-exe/Serva/releases/latest</c>，
/// GitHub 会自动跳转到最新的 Release 页面。
/// </para>
/// </summary>
public sealed class UpdateService : INotifyPropertyChanged
{
    /// <summary>启动检查的最小间隔（避免每次开 App 都发请求）。</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly UpdateChecker _checker;

    private bool _isChecking;
    private bool _hasUpdate;
    private string? _latestVersion;
    private UpdateCheckStatus? _lastStatus;

    private UpdateService()
    {
        _checker = new UpdateChecker();
    }

    /// <summary>全局唯一实例（与 <see cref="SettingsService"/> 同样的单例模式）。</summary>
    public static UpdateService Current { get; } = new();

    /// <summary>最新 Release 页面（界面上「查看更新」链接指向这里）。</summary>
    public static Uri ReleasePageUri => UpdateChecker.LatestReleasePage;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>更新状态发生了变化（检查开始 / 结束、发现新版、忽略、切换语言）。</summary>
    public event EventHandler? Changed;

    /// <summary>是否正在检查更新。</summary>
    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (_isChecking == value)
            {
                return;
            }

            _isChecking = value;
            RaiseStatusChanged();
        }
    }

    /// <summary>是否有「尚未忽略」的新版本。</summary>
    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (_hasUpdate == value)
            {
                return;
            }

            _hasUpdate = value;
            RaiseStatusChanged();
        }
    }

    /// <summary>远程最新版本号（如 <c>0.2.0</c>）；还没检查过时为 <see langword="null"/>。</summary>
    public string? LatestVersion
    {
        get => _latestVersion;
        private set
        {
            if (string.Equals(_latestVersion, value, StringComparison.Ordinal))
            {
                return;
            }

            _latestVersion = value;
            RaiseStatusChanged();
        }
    }

    /// <summary>设置页显示的一行状态文字（按当前语言生成）。</summary>
    public string StatusText
    {
        get
        {
            AppText text = AppText.Current;

            if (IsChecking)
            {
                return text.T("Checking for updates…", "正在检查更新…");
            }

            if (HasUpdate && LatestVersion is { Length: > 0 } version)
            {
                return text.F("Version {0} is available.", "发现新版本 {0}。", version);
            }

            return _lastStatus switch
            {
                UpdateCheckStatus.UpToDate => text.T("You are on the latest version.", "已是最新版本。"),
                UpdateCheckStatus.NoRelease => text.T(
                    "No release has been published yet.",
                    "项目暂未发布任何版本。"),
                UpdateCheckStatus.Failed => text.T(
                    "Could not check for updates. Try again later.",
                    "检查更新失败，请稍后重试。"),
                UpdateCheckStatus.UpdateAvailable when !string.IsNullOrEmpty(LatestVersion) => text.F(
                    "{0} is available but was dismissed.",
                    "已忽略新版本 {0}。",
                    LatestVersion),
                _ => string.Empty,
            };
        }
    }

    /// <summary>状态文字是否需要显示（首次检查之前为空）。</summary>
    public bool HasStatus => !string.IsNullOrEmpty(StatusText);

    /// <summary>
    /// 检查更新。<paramref name="manual"/> = <see langword="true"/> 时跳过节流与「启动时检查」开关。
    /// <para>失败只记录状态，不抛异常、不弹窗。</para>
    /// </summary>
    public async Task CheckAsync(bool manual)
    {
        if (IsChecking)
        {
            return;
        }

        AppSettings settings = SettingsService.Current.Value;

        if (!manual)
        {
            if (!settings.CheckUpdatesOnStartup)
            {
                return;
            }

            if (settings.LastUpdateCheckUtc is { } last && DateTimeOffset.UtcNow - last < CheckInterval)
            {
                return;
            }
        }

        IsChecking = true;

        try
        {
            UpdateCheckResult result = await _checker.CheckAsync(AppVersion.Current);

            _lastStatus = result.Status;
            LatestVersion = result.LatestVersion;

            HasUpdate = result.Status == UpdateCheckStatus.UpdateAvailable
                && !string.Equals(settings.DismissedUpdateVersion, result.LatestVersion, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // 无论成功失败都记录时间：断网 / 被拦截时不要每次启动都重试。
            settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            SettingsService.Current.SaveQuietly();

            IsChecking = false;
        }
    }

    /// <summary>忽略当前提示的版本：记住它，直到出现更新的版本才再次提醒。</summary>
    public void Dismiss()
    {
        if (LatestVersion is not { Length: > 0 } version)
        {
            return;
        }

        SettingsService.Current.Value.DismissedUpdateVersion = version;
        SettingsService.Current.SaveQuietly();

        HasUpdate = false;
        RaiseStatusChanged();
    }

    /// <summary>语言切换后：状态文字按新语言重新生成，并通知界面刷新。</summary>
    public void RefreshTexts() => RaiseStatusChanged();

    private void RaiseStatusChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStatus)));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
