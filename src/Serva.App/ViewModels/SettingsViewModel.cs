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
    private string? _statusMessage;
    private Func<string>? _statusFactory;
    private bool _statusIsError;

    public SettingsViewModel()
    {
        ExcludeRules = [];
        IncludeRules = [];
        Devices = [];

        AddExcludeRuleCommand = new RelayCommand(() => AddRule(isInclude: false), () => CanAddRule);
        AddIncludeRuleCommand = new RelayCommand(() => AddRule(isInclude: true), () => CanAddRule);
        RemoveExcludeRuleCommand = new RelayCommand<string>(rule => RemoveRule(rule, isInclude: false));
        RemoveIncludeRuleCommand = new RelayCommand<string>(rule => RemoveRule(rule, isInclude: true));
        RestoreDefaultRulesCommand = new RelayCommand(RestoreDefaultRules);
        RefreshDevicesCommand = new RelayCommand(RefreshDevices);

        ReloadFromSettings();
        RefreshDevices();
    }

    /// <summary>当前连接的移动存储设备（只读展示）。</summary>
    public ObservableCollection<DeviceInfo> Devices { get; }

    public ObservableCollection<string> ExcludeRules { get; }

    public ObservableCollection<string> IncludeRules { get; }

    public RelayCommand AddExcludeRuleCommand { get; }

    public RelayCommand AddIncludeRuleCommand { get; }

    public RelayCommand<string> RemoveExcludeRuleCommand { get; }

    public RelayCommand<string> RemoveIncludeRuleCommand { get; }

    public RelayCommand RestoreDefaultRulesCommand { get; }

    public RelayCommand RefreshDevicesCommand { get; }

    /// <summary>新增规则输入框内容。</summary>
    public string NewRuleText
    {
        get => _newRuleText;
        set
        {
            if (SetProperty(ref _newRuleText, value))
            {
                AddExcludeRuleCommand.RaiseCanExecuteChanged();
                AddIncludeRuleCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanAddRule));
            }
        }
    }

    public bool CanAddRule => !string.IsNullOrWhiteSpace(NewRuleText);

    /// <summary>操作反馈。</summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public bool StatusIsError
    {
        get => _statusIsError;
        private set
        {
            if (SetProperty(ref _statusIsError, value))
            {
                OnPropertyChanged(nameof(StatusSeverity));
            }
        }
    }

    public InfoBarSeverity StatusSeverity =>
        StatusIsError ? InfoBarSeverity.Error : InfoBarSeverity.Success;

    /// <summary>排除规则数量说明。</summary>
    public string ExcludeCaption
    {
        get
        {
            AppText text = AppText.Current;

            return text.F("{0} exclude rule(s)", "{0} 条排除规则", ExcludeRules.Count)
                + (IncludeRules.Count > 0
                    ? text.F(" · {0} include-only rule(s)", " · {0} 条仅包含规则", IncludeRules.Count)
                    : string.Empty);
        }
    }

    private void SetStatus(Func<string> message, bool isError = false)
    {
        _statusFactory = message;
        StatusIsError = isError;
        StatusMessage = message();
    }

    /// <summary>页面上的「×」：关掉状态提示，切换页面 / 切换语言后都不再出现。</summary>
    public void DismissStatus()
    {
        _statusFactory = null;
        StatusMessage = null;
    }

    /// <summary>语言切换后：本页里「已经算好」的文案重新生成。</summary>
    private void RefreshLocalizedTexts()
    {
        // 只有仍然显示着的状态提示才需要换语言（关掉过的不再复活）。
        if (_statusFactory is not null && StatusMessage is not null)
        {
            StatusMessage = _statusFactory();
        }

        OnPropertyChanged(nameof(ExcludeCaption));
        OnPropertyChanged(nameof(AuthorText));
        OnPropertyChanged(nameof(CurrentVersionText));
        RefreshUpdateState();
    }

    private void Save() => _settings.Save();
}
