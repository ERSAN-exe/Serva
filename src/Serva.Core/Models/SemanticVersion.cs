using System.Globalization;

namespace Serva.Core.Models;

/// <summary>
/// 语义化版本（够用版）：<c>主.次.修订[-预发布][+构建]</c>。
/// <para>
/// 只用于「有没有更新」的比较：允许前导 <c>v</c>、允许省略段（<c>0.2</c> 等于 <c>0.2.0</c>）、
/// 忽略 <c>+构建元数据</c>；带预发布后缀的版本小于同号正式版（<c>1.0.0-beta</c> &lt; <c>1.0.0</c>）。
/// </para>
/// </summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<SemanticVersion>
{
    /// <summary>解析版本号；格式无法识别时返回 <see langword="false"/>。</summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string value = text.Trim();

        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        // 构建元数据（+abc）不参与比较。
        int plus = value.IndexOf('+');
        if (plus >= 0)
        {
            value = value[..plus];
        }

        // 预发布后缀（-beta.2）。
        string? preRelease = null;
        int dash = value.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..].Trim();
            value = value[..dash];

            if (preRelease.Length == 0)
            {
                return false;
            }
        }

        string[] parts = value.Split('.');

        // 允许 0.2 / 0.2.0 / 0.2.0.1（第 4 段只当构建号，不参与比较）。
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        int[] numbers = new int[3];

        for (int index = 0; index < parts.Length && index < 3; index++)
        {
            if (!int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return false;
            }
        }

        if (parts.Length == 4
            && !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    /// <summary>逐段比较主 / 次 / 修订号；同号时按预发布规则比较。</summary>
    public int CompareTo(SemanticVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);

        return result != 0 ? result : ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <summary>正式版 &gt; 预发布版；同为预发布时按段比较（数字段按数值，其余忽略大小写）。</summary>
    private static int ComparePreRelease(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        string[] leftParts = left.Split('.');
        string[] rightParts = right.Split('.');
        int count = Math.Min(leftParts.Length, rightParts.Length);

        for (int index = 0; index < count; index++)
        {
            int compared = CompareIdentifier(leftParts[index], rightParts[index]);
            if (compared != 0)
            {
                return compared;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        bool leftIsNumber = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int leftValue);
        bool rightIsNumber = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int rightValue);

        if (leftIsNumber && rightIsNumber)
        {
            return leftValue.CompareTo(rightValue);
        }

        // 语义化版本规定：数字标识符的优先级低于字母标识符。
        if (leftIsNumber)
        {
            return -1;
        }

        return rightIsNumber ? 1 : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
