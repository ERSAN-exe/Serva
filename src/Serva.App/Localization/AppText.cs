using System.ComponentModel;
using Serva.Core.Services;

namespace Serva.App.Localization;

/// <summary>
/// 界面文案表。
/// <para>
/// 每条文案都是一对「英文 / 简体中文」：默认语言是「跟随系统」（中文系统显示简体中文，其它显示英文），
/// 也可以在设置里显式选择 English / 简体中文。
/// </para>
/// <para>
/// 语言切换时会对所有公开属性触发 <see cref="PropertyChanged"/>，因此
/// <c>Text="{x:Bind Text.Xxx, Mode=OneWay}"</c> 会立即刷新，不需要重开窗口或重新导航。
/// </para>
/// <para>
/// XAML 里用属性（可绑定）；纯代码里的文案直接调用 <see cref="T"/> / <see cref="F"/>，
/// 把两种语言写在用得到的那一行，避免文案表与调用点脱节。
/// </para>
/// </summary>
public sealed class AppText : INotifyPropertyChanged
{
    private static readonly string[] PropertyNames =
        [.. typeof(AppText).GetProperties().Select(static p => p.Name)];

    private bool _chinese;

    /// <summary>全局唯一实例（所有页面共享同一个语言状态）。</summary>
    public static AppText Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>当前是否为简体中文。</summary>
    public bool IsChinese => _chinese;

    /// <summary>取当前语言下的文案（纯代码用）。</summary>
    public string T(string english, string chinese) => _chinese ? chinese : english;

    /// <summary>取当前语言下的文案并填充占位符（两种语言使用同一套 <c>{0}</c> 占位符）。</summary>
    public string F(string english, string chinese, params object?[] args) =>
        string.Format(T(english, chinese), args);

    /// <summary>切换语言：通知所有绑定重新取值。</summary>
    internal void SetLanguage(bool chinese)
    {
        if (_chinese == chinese)
        {
            return;
        }

        _chinese = chinese;

        foreach (string name in PropertyNames)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    // ============================ 外壳 / 导航 ============================

    /// <summary>导航项：比较。</summary>
    public string NavCompare => T("Compare", "比较");

    /// <summary>导航项：设置。</summary>
    public string NavSettings => T("Settings", "设置");

    // ============================ 比较页 ============================

    public string CompareTitle => T("Compare", "比较");

    public string MappingsTitle => T("Mappings", "对应关系");

    public string AddMapping => T("Add mapping", "添加对应关系");

    public string MappingLinkTooltip => T("Local folder ↔ Device folder", "本机文件夹 ↔ 设备文件夹");

    public string RemoveMappingTooltip => T("Remove this mapping", "移除这条对应关系");

    /// <summary>没有选中设备时，右侧「对应关系」里的提示。</summary>
    public string MappingNeedsDevice => T("Select a removable device", "选择一个可移动设备");

    public string DevicesTitle => T("Removable devices", "移动设备");

    public string Rescan => T("Rescan", "重新扫描");

    public string ScanButton => T("Scan", "扫描");

    public string RunCompleted => T("Completed", "已完成");

    public string RunQueued => T("Queued", "队列中");

    public string RunSkipped => T("Skipped", "跳过");

    /// <summary>「跳过」统计块的悬停说明（格子太窄，原因放在提示里）。</summary>
    public string RunSkippedTooltip => T(
        "Files skipped because they were in use or access was denied.",
        "因文件被占用或没有权限而跳过的文件。");

    public string RunFailed => T("Failed", "失败");

    public string CancelRun => T("Stop", "取消执行");

    /// <summary>执行期间「正在执行」弹窗的标题。</summary>
    public string RunningTitle => T("Running", "正在执行");

    // ============================ 日志 ============================

    public string LogTitle => T("Log", "日志");

    public string LogSearchPlaceholder => T("Search by file name", "搜索文件名");

    /// <summary>结果表右上角搜索框的占位提示。</summary>
    public string SearchFileNamePlaceholder => T("Search by file name", "搜索文件名");

    public string ClearLog => T("Clear log", "清除日志");

    public string ClearLogTooltip => T("Delete all logs", "删除全部日志");

    public string ColumnTime => T("Time", "时间");

    public string ColumnPathMessage => T("Path / message", "路径 / 消息");

    public string ColumnOperation => T("Operation", "操作");

    public string ColumnResult => T("Result", "结果");

    // ============================ 比较结果页 ============================

    public string ResultTitle => T("Compare result", "比较结果");

    public string Back => T("Back", "返回");

    public string BackToCompareTooltip => T("Back to the compare page", "返回比较页");

    public string ActionTitle => T("Action", "执行方式");

    public string ActionMerge => T("Merge", "合并");

    public string ActionOverwriteDevice => T("Overwrite device", "覆盖设备");

    public string ActionOverwriteLocal => T("Overwrite local disk", "覆盖本机硬盘");

    public string ActionPolicy => T("Action policy", "执行策略");

    /// <summary>结果表筛选下拉菜单：显示所有（两端完全一致的仍不显示）。</summary>
    public string FilterAll => T("Show all", "显示所有");

    /// <summary>结果表筛选下拉菜单：本机更新。</summary>
    public string FilterLocalNewer => T("Local newer", "本机更新");

    /// <summary>结果表筛选下拉菜单：设备更新。</summary>
    public string FilterDeviceNewer => T("Device newer", "设备更新");

    /// <summary>结果表筛选下拉菜单：仅设备。</summary>
    public string FilterDeviceOnly => T("Device only", "仅设备");

    /// <summary>结果表筛选下拉菜单：仅本机。</summary>
    public string FilterLocalOnly => T("Local only", "仅本机");

    /// <summary>结果表筛选下拉菜单：将被覆盖。</summary>
    public string FilterOverwritten => T("Will be overwritten", "将被覆盖");

    /// <summary>结果表筛选下拉菜单：将删除的项目。</summary>
    public string FilterDeletions => T("To be deleted", "将删除");

    public string ColumnFileName => T("File name", "文件名");

    public string ColumnSize => T("Size", "大小");

    public string ColumnModified => T("Modified", "修改时间");

    public string ColumnStatus => T("Status", "状态");

    public string ColumnAction => T("Action", "将执行");

    public string EmptyRows => T("No items match the filter.", "没有符合条件的项目。");

    /// <summary>「开始执行」前的核对：文件很多时扫描要几十秒，用一个弹窗说明正在比对。</summary>
    public string ComparingFilesTitle => T("Comparing files", "正在比对文件");

    public string Cancel => T("Cancel", "取消");

    public string StartRun => T("Start", "开始执行");

    // ============================ 设置页 ============================

    public string SettingsTitle => T("Settings", "设置");

    /// <summary>设置页「关于」小节的标题。</summary>
    public string AboutSection => T("About", "关于");

    /// <summary>设置页：外观与语言合并成的一张卡片。</summary>
    public string AppearanceLanguageSection => T("Appearance and language", "外观与语言");

    public string ThemeLabel => T("Color mode", "明暗主题");

    public string ThemeSystem => T("Follow system", "跟随系统");

    public string ThemeLight => T("Light", "浅色");

    public string ThemeDark => T("Dark", "深色");

    public string LanguageLabel => T("Display language", "显示语言");

    public string LanguageSystem => T("Follow system", "跟随系统");

    /// <summary>语言选项：简体中文（用自己的语言显示）。</summary>
    public string LanguageChinese => "简体中文";

    /// <summary>语言选项：English（用自己的语言显示）。</summary>
    public string LanguageEnglish => "English";

    public string NoDeviceDetected => T("No removable device detected", "未检测到移动设备");

    public string Refresh => T("Refresh", "刷新");

    public string OptionAllowLocalDrives => T("Allow selecting local disks", "允许选择本机硬盘");

    /// <summary>设置页：允许同时用两块可移动设备（一块做目标，另一块做本机来源）。</summary>
    public string OptionAllowTwoRemovableDevices => T(
        "Allow two removable devices at once",
        "允许同时选择两个移动设备");

    public string OptionMirrorDelete => T("Delete extra files on the target", "删除目标端多余文件");

    public string OptionRecycleBin => T("Move deleted files to the Recycle Bin", "删除时移入系统回收站");

    /// <summary>执行策略：覆盖前把原件挪进 .ServaUndoTemp，撤销时可以还原。</summary>
    public string OptionKeepOverwriteBackup => T(
        "Keep temporary copies of overwritten files for undo",
        "允许覆盖后保留临时副本用于撤销");

    public string ExcludeRulesSection => T("File (folder) rules", "文件（夹）规则");

    /// <summary>规则卡片里第一组小标题：排除规则。</summary>
    public string ExcludeRulesLabel => T("Excluded files", "排除的文件");

    /// <summary>规则卡片里第二组小标题：包含规则（默认含 <c>.minecraft</c>，可以删除）。</summary>
    public string IncludeRulesLabel => T("Included files", "包含的文件");

    public string NewRulePlaceholder => T("New rule, e.g. **/node_modules/**", "新增规则，例如 **/node_modules/**");

    public string AddAsExclude => T("Add as exclude", "添加为排除");

    public string AddAsInclude => T("Add as include", "添加为包含");

    /// <summary>规则校验失败的红色小字（<paramref name="pattern"/> 是用户输入的内容）。</summary>
    public string RuleError(RuleValidationError error, string pattern) => error switch
    {
        RuleValidationError.Empty => T("Enter a rule first.", "请先输入规则内容。"),
        RuleValidationError.TooLong => F(
            "Rule is too long (max {0} characters).",
            "规则过长（最多 {0} 个字符）。",
            RuleValidator.MaxLength),
        RuleValidationError.InvalidCharacters => T(
            "Rule contains invalid characters (such as < > \" | :).",
            "规则含有非法字符（例如 < > \" | :）。"),
        RuleValidationError.WildcardOnly => T(
            "A rule consisting of wildcards only would match everything.",
            "规则不能只由通配符组成。"),
        RuleValidationError.Duplicate => F(
            "Rule \"{0}\" already exists.",
            "规则「{0}」已存在。",
            pattern),
        RuleValidationError.DuplicateInOtherList => F(
            "Rule \"{0}\" is already used by the other list — a rule can't be both excluded and included.",
            "规则「{0}」已经在另一组规则里了（同一条规则不能既排除又包含）。",
            pattern),
        _ => string.Empty,
    };

    // ============================ 固定文件夹 ============================

    public string PinnedFoldersSection => T("Pinned folders", "固定文件夹");

    public string PinnedFoldersHint => T(
        "Pinned folders appear as shortcuts in the \"choose a local folder\" dialog.",
        "固定的文件夹会作为快捷入口出现在「选择本机文件夹」对话框里。");

    public string NoPinnedFolders => T("No pinned folders yet.", "还没有固定任何文件夹。");

    public string AddPinnedFolder => T("Pin a folder", "固定一个文件夹");
    // ============================ 「关于」卡片 ============================

    /// <summary>GitHub 项目地址链接的显示文字。</summary>
    public string ProjectLink => T("Project page", "项目地址");

    // ============================ 更新 ============================

    public string UpdateSection => T("Updates", "更新");

    public string OptionCheckUpdates => T("Check for updates on startup", "启动时检查更新");

    public string CheckForUpdates => T("Check for updates", "检查更新");

    /// <summary>「查看更新」链接（打开 GitHub 上最新的 Release 页面）。</summary>
    public string OpenRelease => T("View release", "查看更新");

    /// <summary>启动时「发现新版本」提示的关闭按钮。</summary>
    public string LaterNotNow => T("Later", "稍后");

    // ============================ 对话框 ============================

    public string UndoDialogTitle => T("Undo this run", "撤销本次运行");

    public string UndoDialogBody => T(
        "Undo deletes the files newly copied by this run. Files that were moved to the Recycle Bin must be restored from the Recycle Bin by yourself.\n"
        + "Overwritten files are restored from the temporary copies kept under .ServaUndoTemp — unless \"Keep temporary copies of overwritten files for undo\" is off, in which case they stay as they are.\n\n"
        + "Continue?",
        "撤销会删除本次运行新复制的文件；移入系统回收站的文件需要你自行从回收站还原。\n"
        + "被覆盖的文件会用 .ServaUndoTemp 里的临时副本还原；若关闭了「允许覆盖后保留临时副本用于撤销」，它们将保持现状、无法还原。\n\n"
        + "是否继续？");

    public string UndoButton => T("Undo", "撤销");

    public string ClearLogDialogTitle => T("Clear log", "清除日志");

    public string ClearLogDialogBody => T(
        "After clearing the log you can no longer undo the last run.\n\nContinue?",
        "清除日志后不能撤销上次操作。\n\n是否继续？");

    public string ClearButton => T("Clear", "清除");

    public string RecycleOffDialogTitle => T(
        "Turn off \"Move deleted files to the Recycle Bin\"?",
        "关闭「删除时移入系统回收站」？");

    public string RecycleOffDialogBody => T(
        "When off, extra files on the target are permanently deleted and cannot be restored from the Recycle Bin.\n\nContinue?",
        "关闭后，目标端多余的文件会被直接永久删除，无法从回收站还原。\n\n是否继续？");

    public string TurnOffButton => T("Turn off", "关闭");

    /// <summary>关闭「保留临时副本」时的确认弹窗（红色标题）。</summary>
    public string BackupOffDialogTitle => T(
        "Turn off keeping copies of overwritten files?",
        "关闭「允许覆盖后保留临时副本用于撤销」？");

    public string BackupOffDialogBody => T(
        "When off, files are overwritten in place and \"Undo this run\" cannot bring the original contents back.\n\nContinue?",
        "关闭后，被覆盖的文件会被直接盖掉，「撤销本次运行」无法还原原来的内容。\n\n是否继续？");

    /// <summary>覆盖前副本空间不足时的确认弹窗（红色标题）。</summary>
    public string BackupSpaceDialogTitle => T(
        "Not enough space to keep undo copies",
        "磁盘空间不足，无法保留撤销副本");

    public string ContinueButton => T("Continue", "继续");

    // ============================ 文件夹浏览对话框 ============================

    public string FolderDialogTitle => T("Select folder", "选择文件夹");

    public string ChooseDeviceFolder => T("Choose a folder on the device", "选择移动设备上的目录");

    public string ChooseLocalFolder => T("Choose a local folder", "选择本机文件夹");

    public string UseCurrentFolder => T("Use this folder", "使用当前文件夹");

    public string BackToDrives => T("Back to drives", "返回盘符选择");

    public string UpOneLevel => T("Up one level", "上一级");

    /// <summary>文件夹选择对话框：上方「设备文件夹」的标题。</summary>
    public string DeviceFolderLabel => T("Device folder", "设备文件夹");

    /// <summary>文件夹选择对话框：下方「本机文件夹」的标题。</summary>
    public string LocalFolderLabel => T("Local folder", "本机文件夹");

    /// <summary>盘符列表页的标题：列表里可能同时有本机硬盘与其它可移动设备。</summary>
    public string LocalDrivesCaption => T("Drives", "驱动器");

    public string ChooseDrivePrompt => T("Select a drive to open", "请选择要进入的驱动器");

    /// <summary>文件夹选择页里的快捷入口：当前用户的文件夹（<c>C:\Users\&lt;用户名&gt;</c>）。</summary>
    public string UserFolder => T("User folder", "用户文件夹");

    /// <summary>不允许选择系统盘根目录（如 <c>C:\</c>）时的说明。</summary>
    public string ProtectedSystemDriveRoot => T(
        "The system drive root can't be used. Pick a folder inside it instead.",
        "不能选择系统盘根目录，请选择它里面的具体文件夹。");

    /// <summary>不允许选择系统文件夹（Windows / Program Files / 回收站 …）时的说明。</summary>
    public string ProtectedSystemFolder => T(
        "System folders can't be used as a sync folder.",
        "系统文件夹不能作为同步目录。");

    /// <summary>在设备上选了设备根目录时的警告标题。</summary>
    public string DeviceRootWarningTitle => T("You picked the device root", "你选择了设备根目录");

    /// <summary>在设备上选了设备根目录时的警告正文（<paramref name="deviceRoot"/> = 设备根目录）。</summary>
    public string DeviceRootWarningBody(string deviceRoot) => F(
        "Everything on {0} will take part in the sync, not just one backup folder — other files on the device may be overwritten or deleted. A dedicated folder on the device is safer.",
        "整块设备（{0}）都会参与同步，不只是某个备份文件夹；设备上的其它文件可能被覆盖或删除。建议先在设备上新建一个专用文件夹。",
        deviceRoot);

    /// <summary>本机来源选了某个硬盘根目录时的警告标题。</summary>
    public string RootFolderWarningTitle => T("You picked a drive root", "你选择了磁盘根目录");

    /// <summary>本机来源选了某个硬盘根目录时的警告正文。</summary>
    public string LocalRootWarningBody(string localRoot) => F(
        "Everything on {0} will take part in the sync, not just one folder — other files on that drive may be read or deleted. A dedicated folder is safer.",
        "整块本机磁盘（{0}）都会参与同步，不只是某个文件夹；那块盘上的其它文件可能被读取或删除。建议另建一个专用文件夹。",
        localRoot);

    /// <summary>设备与本机来源<strong>都</strong>选了根目录时的警告正文（两个盘符都要点明）。</summary>
    public string BothRootsWarningBody(string deviceRoot, string localRoot) => F(
        "Both {0} and {1} will take part in the sync as a whole — everything else on them may be overwritten or deleted. Please pick real folders instead of drive roots.",
        "设备（{0}）与本机磁盘（{1}）两块盘都会整块参与同步，上面的其它文件可能被覆盖或删除。建议两边都选择具体的文件夹，而不是磁盘根目录。",
        deviceRoot,
        localRoot);
}
