using Serva.Core.Models;

namespace Serva.App.ViewModels;

/// <summary>「比较」页视图模型 —— 计划生成、统计与筛选（partial）。</summary>
public sealed partial class CompareViewModel
{
    /// <summary>用当前的扫描结果 + 同步模式 + 执行策略重新生成计划与结果表。</summary>
    private void Replan()
    {
        if (_states.Count == 0)
        {
            return;
        }

        foreach (CompareRowViewModel existing in _allRows)
        {
            existing.SelectionChanged -= OnRowSelectionChanged;
        }

        _allRows.Clear();

        foreach (MappingState state in _states)
        {
            if (state.Left is null || state.Right is null)
            {
                continue;
            }

            CompareResult compare = _compareEngine.Compare(state.Left, state.Right);

            (SyncMode planMode, bool force) = EffectivePlan;
            state.Plan = _planBuilder.Build(compare, planMode, _settings.Options, force);

            foreach (PlanItem item in state.Plan.Items)
            {
                var row = new CompareRowViewModel(item, state.Mapping);
                row.SelectionChanged += OnRowSelectionChanged;
                _allRows.Add(row);
            }
        }

        _plan = _allRows.Count == 0
            ? null
            : new SyncPlan(EffectivePlan.Mode, [.. _allRows.Select(static r => r.Model)]);

        ApplyRows();
        UpdateDerivedSummaries();
    }

    /// <summary>把行装入结果表（按当前筛选方式过滤后）。</summary>
    private void ApplyRows()
    {
        Rows.Clear();
        foreach (CompareRowViewModel row in _allRows)
        {
            if (MatchesFilter(row))
            {
                Rows.Add(row);
            }
        }

        bool all = Rows.Count > 0 && Rows.All(static r => r.IsSelected);
        if (_isAllSelected != all)
        {
            _isAllSelected = all;
            OnPropertyChanged(nameof(IsAllSelected));
        }

        OnPropertyChanged(nameof(HasVisibleRows));
        OnPropertyChanged(nameof(RowsEmptyVisibility));
        OnPropertyChanged(nameof(RowCaptionText));
        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>当前筛选方式是否包含该行（结果页右上角的下拉菜单）。</summary>
    private bool MatchesFilter(CompareRowViewModel row) => _filter switch
    {
        // 显示所有：仍然隐藏两端完全一致的行。
        ResultFilter.Differences => row.DisplayStatus != CompareStatus.Same,

        // 本机更新 / 设备更新：两侧都改过、其中一侧较新。
        ResultFilter.LocalNewer => row.DisplayStatus == CompareStatus.LeftNewer,
        ResultFilter.DeviceNewer => row.DisplayStatus == CompareStatus.RightNewer,

        // 仅设备：设备上有、本机没有。
        ResultFilter.DeviceOnly => row.DisplayStatus == CompareStatus.RightOnly,

        // 仅本机：本机有、设备上没有。
        ResultFilter.LocalOnly => row.DisplayStatus == CompareStatus.LeftOnly,

        // 将被覆盖：目标是「复制到某一侧」，而那一侧本来就有同名文件（会被新内容盖掉）。
        ResultFilter.Overwritten => row.Model.Action switch
        {
            PlanAction.CopyToDevice => row.Model.Item.Right is not null,
            PlanAction.CopyToLocal => row.Model.Item.Left is not null,
            _ => false,
        },

        // 将删除：镜像删除 / 覆盖时目标端多出来的文件。
        ResultFilter.Deletions => row.DisplayStatus == CompareStatus.Delete,

        _ => true,
    };

    /// <summary>丢弃所有扫描结果与计划。</summary>
    private void ResetMappingStates()
    {
        foreach (CompareRowViewModel row in _allRows)
        {
            row.SelectionChanged -= OnRowSelectionChanged;
        }

        _allRows.Clear();
        _states.Clear();
        Rows.Clear();
        _plan = null;

        if (!_isAllSelected)
        {
            _isAllSelected = true;
            OnPropertyChanged(nameof(IsAllSelected));
        }
    }

    private void ResetResults(Func<string> summary)
    {
        ResetMappingStates();

        SetScanSummary(summary);
        UpdateDerivedSummaries();
    }
}
