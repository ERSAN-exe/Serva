using Microsoft.UI.Xaml;
using Serva.Core.Models;
using Serva.Core.Services;

namespace Serva.App.ViewModels;

/// <summary>「设置」页 —— 已连接设备（只读展示）与执行策略。</summary>
public sealed partial class SettingsViewModel
{
    // ============================ 已连接设备 ============================

    private void RefreshDevices()
    {
        Devices.Clear();
        foreach (DeviceInfo device in VolumeInfo.GetDrives(DriveFilter.Removable))
        {
            Devices.Add(device);
        }

        OnPropertyChanged(nameof(DeviceEmptyVisibility));
    }

    /// <summary>没有检测到移动设备时显示一行提示。</summary>
    public Visibility DeviceEmptyVisibility => Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    // ============================ 执行策略 ============================

    /// <summary>
    /// 是否允许把本机硬盘（不含系统盘）也当作同步目标。
    /// 打开后比较页的设备列表里会出现本机的数据盘。
    /// </summary>
    public bool AllowLocalDrives
    {
        get => _settings.Value.AllowLocalDrives;
        set
        {
            if (_settings.Value.AllowLocalDrives == value)
            {
                return;
            }

            _settings.Value.AllowLocalDrives = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool SkipHiddenAndSystem
    {
        get => _settings.Value.Options.SkipHiddenAndSystem;
        set => SetOption(value, _settings.Value.Options.SkipHiddenAndSystem, o => o.SkipHiddenAndSystem = value);
    }

    public bool MirrorDelete
    {
        get => _settings.Value.Options.MirrorDelete;
        set
        {
            if (_settings.Value.Options.MirrorDelete == value)
            {
                return;
            }

            _settings.Value.Options.MirrorDelete = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TrashOptionEnabled));
            Save();
        }
    }

    /// <summary>「移入系统回收站」只在开启镜像删除时才有意义。</summary>
    public bool TrashOptionEnabled => _settings.Value.Options.MirrorDelete;

    public bool UseTrashFolder
    {
        get => _settings.Value.Options.UseTrashFolder;
        set => SetOption(value, _settings.Value.Options.UseTrashFolder, o => o.UseTrashFolder = value);
    }

    private void SetOption(bool value, bool current, Action<SyncOptions> apply)
    {
        if (value == current)
        {
            return;
        }

        apply(_settings.Value.Options);
        OnPropertyChanged();
        Save();
    }
}
