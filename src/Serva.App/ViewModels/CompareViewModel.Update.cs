using Serva.App.Services;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 版本更新提示条（partial）。</summary>
public sealed partial class CompareViewModel
{
    /// <summary>是否有「发现新版本」的提示条。</summary>
    public bool HasUpdateNotice => UpdateService.Current.HasUpdate;

    /// <summary>提示条文案（按当前语言生成）。</summary>
    public string UpdateNoticeText => UpdateService.Current.LatestVersion is { Length: > 0 } version
        ? Text.F(
            "Serva {0} is available. Open the release page to download it.",
            "Serva {0} 已发布，可打开 Release 页面下载。",
            version)
        : string.Empty;

    /// <summary>「查看更新」链接：GitHub 上最新的 Release 页面。</summary>
    public Uri UpdateUri => UpdateService.ReleasePageUri;

    /// <summary>关闭提示条：记住这个版本，之后不再重复提醒。</summary>
    public void DismissUpdateNotice() => UpdateService.Current.Dismiss();

    /// <summary>更新状态变化（检查结束 / 发现新版 / 忽略）后通知界面刷新提示条。</summary>
    private void OnUpdateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasUpdateNotice));
        OnPropertyChanged(nameof(UpdateNoticeText));
        OnPropertyChanged(nameof(UpdateUri));
    }
}
