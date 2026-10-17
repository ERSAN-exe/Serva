using Serva.Core.Models;

namespace Serva.Core.Services;

/// <summary>
/// 冲突判定基线：记录「上一次同步完成后」两端各自的指纹。
/// 当某一相对路径在两端都与基线不同时，说明两端都改动过 → 冲突。
/// </summary>
public sealed class ConflictBaseline
{
    private readonly Dictionary<string, (string Left, string Right)> _map;

    public ConflictBaseline(Dictionary<string, (string Left, string Right)>? map = null)
    {
        _map = map ?? new Dictionary<string, (string Left, string Right)>(StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _map.Count;

    public void Set(string relativePath, FileEntry left, FileEntry right) =>
        _map[relativePath] = (left.Fingerprint, right.Fingerprint);

    public void Set(string relativePath, string leftFingerprint, string rightFingerprint) =>
        _map[relativePath] = (leftFingerprint, rightFingerprint);

    public bool Contains(string relativePath) => _map.ContainsKey(relativePath);

    /// <summary>两端是否都相对基线发生过变化。</summary>
    public bool BothChanged(string relativePath, FileEntry left, FileEntry right)
    {
        if (!_map.TryGetValue(relativePath, out (string Left, string Right) baseline))
        {
            return false;
        }

        bool leftChanged = !string.Equals(baseline.Left, left.Fingerprint, StringComparison.Ordinal);
        bool rightChanged = !string.Equals(baseline.Right, right.Fingerprint, StringComparison.Ordinal);
        return leftChanged && rightChanged;
    }
}
