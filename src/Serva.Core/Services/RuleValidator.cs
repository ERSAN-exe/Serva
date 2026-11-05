namespace Serva.Core.Services;

/// <summary>规则校验结果（设置页据此在输入框下方显示红色小字提示）。</summary>
public enum RuleValidationError
{
    /// <summary>规则可用。</summary>
    None,

    /// <summary>内容为空（或只有空格）。</summary>
    Empty,

    /// <summary>过长。</summary>
    TooLong,

    /// <summary>含文件名里不允许出现的字符（如 <c>&lt; &gt; " | :</c>）。</summary>
    InvalidCharacters,

    /// <summary>只有通配符 / 分隔符（如 <c>**</c>、<c>/</c>）—— 会匹配所有内容。</summary>
    WildcardOnly,

    /// <summary>已经存在同样的规则（忽略大小写）。</summary>
    Duplicate,

    /// <summary>同一条规则已经在另一组规则里（「排除」与「包含」的意思相反，不能两边都有）。</summary>
    DuplicateInOtherList,
}

/// <summary>
/// 新增排除 / 包含规则的校验：给设置页判断「这条规则能不能加」，并据此显示红字提示。
/// <para>纯函数，不依赖界面；规则语法与 <see cref="ExcludeMatcher"/> 一致（glob：<c>*</c> / <c>**</c> / <c>?</c>）。</para>
/// </summary>
public static class RuleValidator
{
    /// <summary>规则允许的最大长度（与 Windows 路径长度上限一致）。</summary>
    public const int MaxLength = 260;

    /// <summary>文件名里不允许出现的字符。</summary>
    private static readonly char[] ForbiddenCharacters = ['<', '>', '"', '|', ':', '\0'];

    /// <summary>
    /// 校验一条规则；<paramref name="existing"/> 是已经有的规则（用于判重，忽略大小写）。
    /// </summary>
    public static RuleValidationError Validate(string? pattern, IEnumerable<string>? existing = null)
    {
        string rule = (pattern ?? string.Empty).Trim();

        if (rule.Length == 0)
        {
            return RuleValidationError.Empty;
        }

        if (rule.Length > MaxLength)
        {
            return RuleValidationError.TooLong;
        }

        foreach (char c in rule)
        {
            if (char.IsControl(c) || Array.IndexOf(ForbiddenCharacters, c) >= 0)
            {
                return RuleValidationError.InvalidCharacters;
            }
        }

        // 只有通配符 / 分隔符（**、/、?/ 之类）会命中任何内容，等于把整盘都排除掉。
        if (!rule.Any(static c => c != '*' && c != '?' && c != '/' && c != '\\' && !char.IsWhiteSpace(c)))
        {
            return RuleValidationError.WildcardOnly;
        }

        if (existing is not null)
        {
            string normalized = ExcludeMatcher.Normalize(rule);

            foreach (string item in existing)
            {
                if (string.Equals(ExcludeMatcher.Normalize(item.Trim()), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return RuleValidationError.Duplicate;
                }
            }
        }

        return RuleValidationError.None;
    }
}
