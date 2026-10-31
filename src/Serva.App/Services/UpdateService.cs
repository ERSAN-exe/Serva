using System.ComponentModel;
using Serva.App.Localization;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.Services;

/// <summary>
/// 版本更新提醒。
/// <para>
/// <strong>每次启动</strong>都在后台检查一次（并发保护：上一次还没结束时不会再发请求），
/// 发现更新的正式版本时通过 <see cref="Changed"/> 通知界面：启动时的弹窗提示、
/// 比较页顶部的提示条、「设置」页的更新卡片、导航项上的小红点。
/// </para>
/// <para>
/// 上次查到的版本号会随设置一起保存，因此打开软件后即使还没联网，也能立刻显示「发现新版本」。
/// </para>
/// <para>
/// 「查看更新」统一打开 <c>https://github.com/ERSAN-exe/Serva/releases/latest</c>，
/// GitHub 会自动跳转到最新的 Release 页面。
/// </para>
/// </summary>
public sealed class UpdateService : INotifyPropertyChanged
{
    private readonly UpdateChecker _checker;

    private bool _isChecking;
    private bool _hasUpdate;
    private string? _latestVersion;
    private UpdateCheckStatus? _lastStatus;

    private UpdateService()
    {
        _checker = new UpdateChecker();
        RestoreLastKnownVersion();
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
    /// 检查更新。<paramref name="manual"/> = <see langword="true"/> 时跳过「启动时检查」开关。
    /// <para>每次启动都会调用一次；失败只记录状态，不抛异常、不弹窗。</para>
    /// </summary>
    public async Task CheckAsync(bool manual)
    {
        if (IsChecking)
        {
            return;
        }

        AppSettings settings = SettingsService.Current.Value;

        if (!manual && !settings.CheckUpdatesOnStartup)
        {
            return;
        }

        IsChecking = true;

        try
        {
            UpdateCheckResult result = await _checker.CheckAsync(AppVersion.Current);

            _lastStatus = result.Status;

            // 检查失败时不动上一次的结果：本地已知的版本号仍然有效，提示条不必消失。
            if (result.LatestVersion is { Length: > 0 } latest)
            {
                LatestVersion = latest;
                settings.LatestKnownVersion = latest;

                HasUpdate = result.Status == UpdateCheckStatus.UpdateAvailable
                    && !string.Equals(settings.DismissedUpdateVersion, latest, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            // 无论成功失败都记录时间：断网 / 被拦截时，几分钟内反复启动不必重复请求。
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

    /// <summary>
    /// 用上次检查到的最新版本号先把状态补上：打开软件后立刻能显示提示条 / 导航小红点，
    /// 不必等这一轮联网检查（断网时也照样提示上次查到的结果）。
    /// </summary>
    private void RestoreLastKnownVersion()
    {
        AppSettings settings = SettingsService.Current.Value;

        if (settings.LatestKnownVersion is not { Length: > 0 } known)
        {
            return;
        }

        LatestVersion = known;

        HasUpdate = IsNewerThanCurrent(known)
            && !string.Equals(settings.DismissedUpdateVersion, known, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <paramref name="version"/> 是否比当前运行的版本新；
    /// 版本号格式认不出来时按「不是新版」处理（不打扰用户）。
    /// </summary>
    private static bool IsNewerThanCurrent(string version) =>
        SemanticVersion.TryParse(AppVersion.Current, out SemanticVersion current)
        && SemanticVersion.TryParse(version, out SemanticVersion latest)
        && latest.CompareTo(current) > 0;

    private void RaiseStatusChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasStatus)));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
