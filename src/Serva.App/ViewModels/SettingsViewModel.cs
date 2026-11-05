using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using Serva.App.Localization;
using Serva.App.Services;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>
/// 「设置」页视图模型：外观 / 已连接设备 / 执行策略 / 排除规则。
/// <para>所有改动<strong>立即保存</strong>到共享的 <see cref="SettingsService"/>。</para>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settings = SettingsService.Current;

    private string _newRuleText = string.Empty;

    public SettingsViewModel()
    {
        ExcludeRules = [];
        IncludeRules = [];
        Devices = [];

        AddExcludeRuleCommand = new RelayCommand(() => AddRule(isInclude: false), () => CanAddRule);
        AddIncludeRuleCommand = new RelayCommand(() => AddRule(isInclude: true), () => CanAddRule);
        RemoveExcludeRuleCommand = new RelayCommand<string>(rule => RemoveRule(rule, isInclude: false));
        RemoveIncludeRuleCommand = new RelayCommand<string>(rule => RemoveRule(rule, isInclude: true));
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);

        ReloadFromSettings();
        RefreshDevices();
    }

    /// <summary>当前连接的移动存储设备（只读展示）。</summary>
    public ObservableCollection<DeviceInfo> Devices { get; }

    /// <summary>排除规则（内置规则带「不可删除」标记）。</summary>
    public ObservableCollection<ExcludeRuleViewModel> ExcludeRules { get; }

    public ObservableCollection<string> IncludeRules { get; }

    public RelayCommand AddExcludeRuleCommand { get; }

    public RelayCommand AddIncludeRuleCommand { get; }

    public RelayCommand<string> RemoveExcludeRuleCommand { get; }

    public RelayCommand<string> RemoveIncludeRuleCommand { get; }

    public RelayCommand RefreshDevicesCommand { get; }

    /// <summary>
    /// 语言切换后、本页「已经算好的文案」全部重新生成完时触发。
    /// <para>
    /// 页面订阅它做纯界面层的收尾：<c>ComboBox</c> 收起时显示的文案取自选项内容，
    /// 选项内容换了新语言后它不会自己重新取值（见 <c>SettingsPage.RefreshComboBoxTexts</c>）。
    /// </para>
    /// </summary>
    public event EventHandler? LocalizedTextsRefreshed;

    /// <summary>新增规则输入框内容。</summary>
    public string NewRuleText
    {
        get => _newRuleText;
        set
        {
            if (SetProperty(ref _newRuleText, value))
            {
                // 重新输入就把上次的红字提示收掉。
                ClearRuleError();
                AddExcludeRuleCommand.RaiseCanExecuteChanged();
                AddIncludeRuleCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanAddRule));
            }
        }
    }

    public bool CanAddRule => !string.IsNullOrWhiteSpace(NewRuleText);

    /// <summary>规则数量说明：排除几条、包含几条。</summary>
    public string ExcludeCaption =>
        AppText.Current.F(
            "Excluded: {0} · Included: {1}",
            "排除规则 {0} 条 · 包含规则 {1} 条",
            ExcludeRules.Count,
            IncludeRules.Count);

    /// <summary>语言切换后：本页里「已经算好」的文案重新生成。</summary>
    private void RefreshLocalizedTexts()
    {
        // 规则标签里有内置规则的悬停说明：按新语言重新放一遍列表。
        RebuildRuleLists();

        // 设备卡片用一次性绑定，换成新实例才会按新语言重新取值。
        RefreshDeviceTexts();

        OnPropertyChanged(nameof(ExcludeCaption));
        OnPropertyChanged(nameof(RuleErrorText));
        OnPropertyChanged(nameof(AuthorText));
        OnPropertyChanged(nameof(CurrentVersionText));
        RefreshUpdateState();

        // 界面层的收尾（下拉框收起时显示的文案不会自己刷新，见 SettingsPage）。
        LocalizedTextsRefreshed?.Invoke(this, EventArgs.Empty);
    }

    private void Save() => _settings.Save();
}
