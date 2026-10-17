using Serva.App.Localization;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「设置」页 —— 排除规则。</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>把当前设置重新读入所有绑定属性。</summary>
    private void ReloadFromSettings()
    {
        RebuildRuleLists();
        NewRuleText = string.Empty;
        OnPropertyChanged(string.Empty);
    }

    private void RebuildRuleLists()
    {
        ExcludeRules.Clear();
        foreach (string rule in _settings.Value.Options.ExcludePatterns)
        {
            ExcludeRules.Add(rule);
        }

        IncludeRules.Clear();
        foreach (string rule in _settings.Value.Options.IncludePatterns)
        {
            IncludeRules.Add(rule);
        }

        OnPropertyChanged(nameof(ExcludeCaption));
    }

    private void AddRule(bool isInclude)
    {
        string rule = NewRuleText.Trim();
        if (rule.Length == 0)
        {
            return;
        }

        List<string> target = isInclude
            ? _settings.Value.Options.IncludePatterns
            : _settings.Value.Options.ExcludePatterns;

        if (target.Any(r => string.Equals(r, rule, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus(
                () => AppText.Current.F("Rule \"{0}\" already exists.", "规则「{0}」已经存在。", rule),
                isError: true);
            return;
        }

        target.Add(rule);
        NewRuleText = string.Empty;
        RebuildRuleLists();
        Save();
        SetStatus(() => AppText.Current.F("Added: {0}", "已添加：{0}", rule));
    }

    private void RemoveRule(string? rule, bool isInclude)
    {
        if (string.IsNullOrEmpty(rule))
        {
            return;
        }

        List<string> target = isInclude
            ? _settings.Value.Options.IncludePatterns
            : _settings.Value.Options.ExcludePatterns;

        if (target.RemoveAll(r => string.Equals(r, rule, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            return;
        }

        RebuildRuleLists();
        Save();
        SetStatus(() => AppText.Current.F("Removed: {0}", "已移除：{0}", rule));
    }

    private void RestoreDefaultRules()
    {
        _settings.Value.Options.ExcludePatterns = [.. ExcludeMatcher.DefaultExcludePatterns];
        _settings.Value.Options.IncludePatterns = [];
        RebuildRuleLists();
        Save();
        SetStatus(() => AppText.Current.F(
            "Restored {0} built-in exclude rule(s).",
            "已恢复内置的 {0} 条排除规则。",
            ExcludeMatcher.DefaultExcludePatterns.Count));
    }
}
