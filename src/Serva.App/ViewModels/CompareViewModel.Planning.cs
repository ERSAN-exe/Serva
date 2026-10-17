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

    /// <summary>把行装入结果表（按「只显示有差异项目」过滤后）。</summary>
    private void ApplyRows()
    {
        Rows.Clear();
        foreach (CompareRowViewModel row in _allRows)
        {
            if (_showOnlyDifferences && row.DisplayStatus == CompareStatus.Same)
            {
                continue;
            }

            Rows.Add(row);
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
