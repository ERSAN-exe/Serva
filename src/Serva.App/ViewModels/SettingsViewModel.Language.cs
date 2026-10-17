using Serva.App.Services;

namespace Serva.App.ViewModels;

/// <summary>「设置」页 —— 语言（跟随系统 / 简体中文 / English）。</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>0 = 跟随系统，1 = 简体中文，2 = English（绑定 RadioButtons）。</summary>
    public int LanguageIndex
    {
        get => LanguageService.IndexOf(_settings.Value.Language);
        set
        {
            string language = LanguageService.FromIndex(value);

            if (string.Equals(_settings.Value.Language, language, StringComparison.Ordinal))
            {
                return;
            }

            _settings.Value.Language = language;

            // 先落盘再切界面：切语言会把整个界面的文案重建一遍。
            Save();
            LanguageService.Apply(_settings.Value);

            // 本页里「已经算好」的文案（规则统计、关于信息、状态提示）重新生成。
            RefreshLocalizedTexts();

            // 比较页 / 结果页 / 日志页里的计划文案与日志行由 App 统一通知刷新。
            App.OnLanguageChanged();
        }
    }
}
