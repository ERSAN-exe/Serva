using System.Text.Json;
using System.Text.Json.Serialization;
using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 应用设置读写：<c>%LocalAppData%\Serva\settings.json</c>。
/// 支持导出 / 导入 / 恢复默认。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SettingsStore(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? DefaultSettingsPath;
    }

    public string SettingsPath { get; }

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Serva",
        "settings.json");

    /// <summary>读取设置；文件缺失或损坏时返回默认值。</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using FileStream stream = File.OpenRead(SettingsPath);
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions);
                if (settings is not null)
                {
                    settings.Options ??= new SyncOptions();
                    MigrateLegacyMappings(settings);
                    return settings;
                }
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new AppSettings();
    }

    /// <summary>
    /// 旧版设置文件只有一个全局 <see cref="AppSettings.Mappings"/>：搬到「上次使用的那台设备」名下，
    /// 之后每台设备各存各的，切换设备不会串台。
    /// <para>
    /// 没有设备信息时搬不了，就原样留着（读设备根目录里的 <c>.serva-mappings.json</c> 仍能找回）。
    /// </para>
    /// </summary>
    private static void MigrateLegacyMappings(AppSettings settings)
    {
        if (settings.Mappings is not { Count: > 0 })
        {
            return;
        }

        string? key = settings.LastTargetIdentity;
        if (string.IsNullOrEmpty(key))
        {
            key = settings.LastTargetRoot;
        }

        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        settings.SetMappingsForDevice(key, settings.LastTargetRoot ?? string.Empty, null, settings.Mappings);

        // 搬完就清空：旧列表如果还在，以后可能又被当成「上次使用那台设备」的对应关系读回来。
        settings.Mappings = [];
    }

    public void Save(AppSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using FileStream stream = File.Create(SettingsPath);
            JsonSerializer.Serialize(stream, settings, JsonOptions);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>恢复默认设置（删除设置文件）。</summary>
    public AppSettings Reset()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new AppSettings();
    }

    /// <summary>导出设置到指定文件。</summary>
    public void Export(string destinationPath, AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(destinationPath);
        JsonSerializer.Serialize(stream, settings, JsonOptions);
    }

    /// <summary>从指定文件导入设置，并写入当前设置文件。</summary>
    public AppSettings Import(string sourcePath)
    {
        using FileStream stream = File.OpenRead(sourcePath);
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        settings.Options ??= new SyncOptions();
        MigrateLegacyMappings(settings);
        Save(settings);
        return settings;
    }
}
