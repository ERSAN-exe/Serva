using Microsoft.UI.Xaml;
using Serva.App.Localization;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「设置」页 —— 执行规则（排除规则 / 包含规则）。</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>最近一次校验失败的原因（输入框下方红字用）。</summary>
    private RuleValidationError _ruleError;

    /// <summary>触发该错误时输入框里的内容（「规则已存在」要把它显示出来）。</summary>
    private string _ruleErrorPattern = string.Empty;

    /// <summary>输入框下方的红色小字；没有错误时为空。</summary>
    public string RuleErrorText => _ruleError == RuleValidationError.None
        ? string.Empty
        : AppText.Current.RuleError(_ruleError, _ruleErrorPattern);

    /// <summary>红色小字是否显示。</summary>
    public Visibility RuleErrorVisibility =>
        _ruleError == RuleValidationError.None ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>把当前设置重新读入所有绑定属性。</summary>
    private void ReloadFromSettings()
    {
        RebuildRuleLists();
        RebuildPinnedFolders();
        NewRuleText = string.Empty;
        ClearRuleError();
        OnPropertyChanged(string.Empty);
    }

    private void RebuildRuleLists()
    {
        ExcludeRules.Clear();
        foreach (string rule in _settings.Value.Options.ExcludePatterns)
        {
            bool isBuiltIn = ExcludeMatcher.DefaultExcludePatterns
                .Contains(rule, StringComparer.OrdinalIgnoreCase);

            ExcludeRules.Add(new ExcludeRuleViewModel(rule, isBuiltIn));
        }

        // 包含规则：先列内置清单（默认为空，用户可以删），后面才是用户自己加的规则。
        IncludeRules.Clear();
        foreach (string rule in _settings.Value.Options.AlwaysIncludePatterns
                     .Concat(_settings.Value.Options.IncludePatterns))
        {
            if (!IncludeRules.Contains(rule, StringComparer.OrdinalIgnoreCase))
            {
                IncludeRules.Add(rule);
            }
        }

        OnPropertyChanged(nameof(ExcludeCaption));
    }

    private void AddRule(bool isInclude)
    {
        string rule = NewRuleText.Trim();

        // 重复 / 无效的规则不加进列表，只在输入框下方显示红色小字。
        RuleValidationError error = ValidateNewRule(rule, isInclude);

        if (error != RuleValidationError.None)
        {
            SetRuleError(error, rule);
            return;
        }

        if (isInclude)
        {
            // 「包含的文件」= 额外包含：命中的文件一定同步（隐藏也照扫），其余文件照旧按排除规则处理。
            // 不能写进 IncludePatterns —— 那是白名单，加了规则会让别的文件全部消失。
            _settings.Value.Options.AlwaysIncludePatterns.Add(rule);
        }
        else
        {
            _settings.Value.Options.ExcludePatterns.Add(rule);
        }

        RebuildRuleLists();
        Save();
        NewRuleText = string.Empty;   // 清空输入框（顺手把红字提示一起收掉）
    }

    private void RemoveRule(string? rule, bool isInclude)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return;
        }

        if (isInclude)
        {
            // 「包含规则」里既有内置清单也有用户添加的规则，两处都试着删。
            int removed = _settings.Value.Options.AlwaysIncludePatterns.RemoveAll(MatchRule(rule))
                          + _settings.Value.Options.IncludePatterns.RemoveAll(MatchRule(rule));

            if (removed == 0)
            {
                return;
            }
        }
        else
        {
            // 内置排除规则不可删除（界面上也不给删除按钮，这里再兜一层）。
            if (ExcludeMatcher.DefaultExcludePatterns.Contains(rule, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            if (_settings.Value.Options.ExcludePatterns.RemoveAll(MatchRule(rule)) == 0)
            {
                return;
            }
        }

        RebuildRuleLists();
        Save();
    }

    /// <summary>要判重的那一组规则（添加「包含」时，内置清单也算进去）。</summary>
    private IEnumerable<string> RulesIn(bool isInclude) => isInclude
        ? IncludeRules
        : ExcludeRules.Select(static r => r.Pattern);

    /// <summary>
    /// 判断一条新规则能不能加：
    /// <list type="number">
    /// <item>先在这一组里判重、检查长度与非法字符；</item>
    /// <item>再确认另一组里没有它 —— 「排除」和「包含」是相反的意思，同一条规则不能两边都占。</item>
    /// </list>
    /// </summary>
    private RuleValidationError ValidateNewRule(string rule, bool isInclude)
    {
        RuleValidationError error = RuleValidator.Validate(rule, RulesIn(isInclude));

        if (error != RuleValidationError.None)
        {
            return error;
        }

        return RuleValidator.Validate(rule, RulesIn(!isInclude)) == RuleValidationError.Duplicate
            ? RuleValidationError.DuplicateInOtherList
            : RuleValidationError.None;
    }

    private static Predicate<string> MatchRule(string rule) =>
        pattern => string.Equals(pattern, rule, StringComparison.OrdinalIgnoreCase);

    private void SetRuleError(RuleValidationError error, string pattern)
    {
        _ruleError = error;
        _ruleErrorPattern = pattern;
        OnPropertyChanged(nameof(RuleErrorText));
        OnPropertyChanged(nameof(RuleErrorVisibility));
    }

    private void ClearRuleError()
    {
        if (_ruleError == RuleValidationError.None)
        {
            return;
        }

        _ruleError = RuleValidationError.None;
        _ruleErrorPattern = string.Empty;
        OnPropertyChanged(nameof(RuleErrorText));
        OnPropertyChanged(nameof(RuleErrorVisibility));
    }
}
