namespace Serva.App.ViewModels;

/// <summary>执行方式里的一张「盘符卡片」：小字角色（本机硬盘 / 外置设备 + 卷标）+ 盘符。</summary>
public sealed record DriveCardViewModel(string RoleText, string DriveText);
