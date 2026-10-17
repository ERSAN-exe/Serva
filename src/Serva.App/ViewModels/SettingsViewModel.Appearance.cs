using Serva.App.Services;

namespace Serva.App.ViewModels;

/// <summary>「设置」页 —— 外观（明暗主题）。</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>0 = 跟随系统，1 = 浅色，2 = 深色（绑定 RadioButtons）。</summary>
    public int ThemeIndex
    {
        get => AppearanceService.NormalizeTheme(_settings.Value.Theme) switch
        {
            AppearanceService.LightTheme => 1,
            AppearanceService.DarkTheme => 2,
            _ => 0,
        };
        set
        {
            string theme = value switch
            {
                1 => AppearanceService.LightTheme,
                2 => AppearanceService.DarkTheme,
                _ => AppearanceService.SystemTheme,
            };

            if (_settings.Value.Theme == theme)
            {
                return;
            }

            _settings.Value.Theme = theme;
            OnPropertyChanged();
            SaveAndApplyAppearance();
        }
    }

    private void SaveAndApplyAppearance()
    {
        Save();

        if (App.MainWindow is { } window)
        {
            AppearanceService.Apply(window, _settings.Value);
        }
    }
}
