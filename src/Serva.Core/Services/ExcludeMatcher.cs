using System.Text;
using System.Text.RegularExpressions;

namespace Serva.Core.Services;

/// <summary>
/// 排除 / 仅包含规则匹配器。
/// <para>规则语法（glob）：<c>*</c> 匹配单个路径段内的任意字符；<c>**</c> 跨越路径分隔符；<c>?</c> 匹配单个字符。</para>
/// <para>不含 <c>/</c> 的规则（如 <c>Thumbs.db</c>）会按「任意路径段」匹配，因此可命中任意深度的同名文件 / 目录。</para>
/// </summary>
public sealed class ExcludeMatcher
{
    /// <summary>
    /// 内置的默认排除规则（原样稿 12 条 + Serva 自身文件规则 + macOS 外置盘上的元数据目录）。
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExcludePatterns =
    [
        "System Volume Information",
        "$RECYCLE.BIN",
        "$Recycle.Bin",
        "_ServaTrash",
        "Thumbs.db",
        "desktop.ini",
        ".DS_Store",
        "._*",
        // macOS 在外置盘上生成的索引目录（一般带隐藏属性，点名再挡一次）。
        ".Spotlight-V100",
        ".fseventsd",
        "pagefile.sys",
        "hiberfil.sys",
        "swapfile.sys",
        "*.tmp",
        // Serva 自身的半成品文件（写入采用 <目标>.serva-part + 原子改名，
        // 崩溃后残留的半截文件必须被忽略，否则会被误判为「已存在且相同」）。
        "*.serva-part",
        // 写在移动设备根目录下的对应关系文件（隐藏文件），不能被同步或镜像删除。
        DeviceMappingStore.FileName,
        // 覆盖前保留原件的临时目录（隐藏），既不能同步也不能被镜像删除。
        OverwriteBackup.FolderName,
    ];

    /// <summary>
    /// 内置的「包含」规则默认值（对应 <see cref="Models.SyncOptions.AlwaysIncludePatterns"/>）。
    /// <para>
    /// <strong>默认为空</strong>：不预置任何包含规则（以前预置过 <c>.minecraft</c>）。
    /// 用户需要时在「设置 → 执行规则 → 包含规则」里自己添加，添加后可以随时删除，删掉不会再自动补回。
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultIncludedPatterns = [];

    private readonly Regex[] _exclude;
    private readonly Regex[] _include;
    private readonly Regex[] _alwaysIncluded;

    /// <summary>
    /// 各组规则里是否含 <c>/</c>（即「按路径」写的规则）。
    /// <para>没有这种规则时，单个路径段匹配已经覆盖了整棵子树，不必再逐级检查上级目录（大目录下这是实打实的开销）。</para>
    /// </summary>
    private readonly bool _excludeHasNested;
    private readonly bool _includeHasNested;
    private readonly bool _alwaysIncludedHasNested;

    /// <summary>
    /// 每条「包含」规则的字面前缀（第一个通配符之前的部分）、规则后面是否还带通配符、以及是否是「名字」规则
    /// （不含 <c>/</c>，例如 <c>HiddenDir</c>、<c>*.log</c> —— 这种规则按任意层级的路径段匹配）。
    /// <para>
    /// 用来判断某个目录是不是规则的<strong>上级目录</strong>：<c>sub/hidden</c> 这条规则要求扫描器
    /// 先进到 <c>sub</c>，只按「这条路径是否命中规则」判断会把 <c>sub</c> 剪掉，规则永远走不到目标。
    /// </para>
    /// </summary>
    private readonly (string Prefix, bool HasWildcard, bool NameOnly)[] _includeScopes;

    /// <summary>同上，对应「包含的文件」清单（<see cref="Models.SyncOptions.AlwaysIncludePatterns"/>）。</summary>
    private readonly (string Prefix, bool HasWildcard, bool NameOnly)[] _alwaysIncludeScopes;

    /// <summary>
    /// 用户「仅包含」里是否有只按名字写的规则（不含 <c>/</c>）。
    /// <para>
    /// 这种规则可能命中任意层级的同名文件夹，扫描器没法提前剪枝 —— 必须一路下探去找。
    /// </para>
    /// </summary>
    private readonly bool _includeHasNameOnlyRule;

    public ExcludeMatcher(
        IEnumerable<string>? excludePatterns = null,
        IEnumerable<string>? includePatterns = null,
        IEnumerable<string>? alwaysIncludePatterns = null)
    {
        string[] excludes = [.. excludePatterns ?? DefaultExcludePatterns];
        string[] includes = [.. includePatterns ?? []];
        string[] alwaysIncludes = [.. alwaysIncludePatterns ?? DefaultIncludedPatterns];

        _exclude = Compile(excludes);
        _include = Compile(includes);
        _alwaysIncluded = Compile(alwaysIncludes);
        _includeScopes = BuildIncludeScopes(includes);
        _alwaysIncludeScopes = BuildIncludeScopes(alwaysIncludes);
        _includeHasNameOnlyRule = _includeScopes.Any(static scope => scope.NameOnly);

        _excludeHasNested = HasNestedRule(excludes);
        _includeHasNested = HasNestedRule(includes);
        _alwaysIncludedHasNested = HasNestedRule(alwaysIncludes);
    }

    public int ExcludeCount => _exclude.Length;

    public int IncludeCount => _include.Length;

    /// <summary>
    /// 判断相对路径（文件或目录）是否应被忽略。
    /// <para>
    /// 顺序：先看「包含」规则（命中即<strong>不排除</strong>，见
    /// <see cref="DefaultIncludedPatterns"/>），再看仅包含白名单，最后看排除规则。
    /// </para>
    /// </summary>
    public bool IsExcluded(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        string normalized = Normalize(relativePath);
        if (normalized.Length == 0)
        {
            return false;
        }

        // 目录规则通常以 "dir/" 或 "dir/**" 结尾，故额外用带尾部斜杠的形式再试一次。
        string withSlash = normalized + "/";
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // 「包含」规则优先：用户加进来就是明确要它，不能被排除规则挡掉。
        if (AnyMatchWithAncestors(_alwaysIncluded, _alwaysIncludedHasNested, normalized, withSlash, segments))
        {
            return false;
        }

        if (_include.Length > 0
            && !AnyMatchWithAncestors(_include, _includeHasNested, normalized, withSlash, segments))
        {
            return true;
        }

        return AnyMatchWithAncestors(_exclude, _excludeHasNested, normalized, withSlash, segments);
    }

    /// <summary>
    /// 该相对路径是否被<strong>显式包含</strong>：命中「包含规则」、
    /// 位于被点名的文件夹之下（文件夹规则连带整棵子树），或命中内置清单。
    /// <para>
    /// 扫描器用它决定「隐藏 / 系统文件（夹）」要不要跳过 ——
    /// 用户特意把某个隐藏文件夹加成包含时，就不是要忽略它，而是只要它。
    /// </para>
    /// </summary>
    public bool IsExplicitlyIncluded(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        string normalized = Normalize(relativePath);
        if (normalized.Length == 0)
        {
            return false;
        }

        string withSlash = normalized + "/";
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return (_include.Length > 0
                && AnyMatchWithAncestors(_include, _includeHasNested, normalized, withSlash, segments))
               || AnyMatchWithAncestors(
                   _alwaysIncluded, _alwaysIncludedHasNested, normalized, withSlash, segments);
    }

    /// <summary>
    /// 该路径是否「可能含有被包含的内容」：它自己（或它的上级文件夹）被包含，
    /// 或者它是某条「包含」规则的<strong>上级目录</strong>（如规则 <c>sub/hidden</c> 里的 <c>sub</c>）。
    /// <para>
    /// 扫描器用它决定隐藏目录要不要继续下探 —— 隐藏上级目录不进去，里面的目标文件夹就永远看不到。
    /// </para>
    /// </summary>
    public bool MayContainIncluded(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        string normalized = Normalize(relativePath);
        if (normalized.Length == 0)
        {
            return false;
        }

        return IsExplicitlyIncluded(normalized) || IsAboveInclude(normalized, withWildcards: true);
    }

    /// <summary>
    /// 扫描目录时是否还要继续往下走。
    /// <para>
    /// 判断顺序：被包含（含上级文件夹被包含）→ 「包含」规则点名了这棵子树里的某个目录（必须进去才走得到）
    /// → 被排除规则点名（整棵子树跳过）→ 没有「仅包含」白名单（照常遍历）→
    /// 还可能是某条规则的上级目录（如规则 <c>sub/hidden</c> 里的 <c>sub</c>）。
    /// </para>
    /// <para>最后一步是关键：只按「这条路径是否命中白名单」判断会把上级目录剪掉，规则永远走不到目标。</para>
    /// </summary>
    public bool ShouldDescend(string relativeDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativeDirectory))
        {
            return true;
        }

        string normalized = Normalize(relativeDirectory);
        if (normalized.Length == 0)
        {
            return true;
        }

        string withSlash = normalized + "/";
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // 被包含的目录（含「上级文件夹被包含」）：一定进去。
        if (AnyMatchWithAncestors(
                _alwaysIncluded, _alwaysIncludedHasNested, normalized, withSlash, segments)
            || (_include.Length > 0
                && AnyMatchWithAncestors(_include, _includeHasNested, normalized, withSlash, segments)))
        {
            return true;
        }

        // 「包含」规则点名了这棵子树里的某个目录：为了走到它，即使本级被排除规则挡住也要进去。
        if (IsAboveInclude(normalized, withWildcards: false))
        {
            return true;
        }

        if (AnyMatchWithAncestors(_exclude, _excludeHasNested, normalized, withSlash, segments))
        {
            return false;
        }

        if (_include.Length == 0)
        {
            return true;
        }

        // 白名单没命中的目录：只有「还可能走到某条规则」时才继续下探（带通配符的规则尤其如此）。
        return IsAboveInclude(normalized, withWildcards: true);
    }

    /// <summary>
    /// 该目录是不是某条「包含」规则的上级目录。
    /// <para>
    /// <paramref name="withWildcards"/> 为 <c>false</c> 时只看「字面上一定能走到目标」的情形
    /// （目录是规则字面前缀的上级）；为 <c>true</c> 时连「可能走到」的情形也算上
    /// （规则以通配符开头 / 只按名字写，或目录位于带通配符的规则的字面前缀之下）。
    /// </para>
    /// </summary>
    private bool IsAboveInclude(string normalized, bool withWildcards)
    {
        // 「仅包含」里只写了名字的规则（如 HiddenDir、*.log）：它可能出现在任意层级的子目录里，
        // 没法提前剪枝，只能一路下探去找 —— 否则隐藏文件夹藏在子目录里就永远搜不到。
        if (withWildcards && _includeHasNameOnlyRule)
        {
            return true;
        }

        return IsAboveScopes(_includeScopes, normalized, withWildcards)
               || IsAboveScopes(_alwaysIncludeScopes, normalized, withWildcards);
    }

    private static bool IsAboveScopes(
        (string Prefix, bool HasWildcard, bool NameOnly)[] scopes,
        string normalized,
        bool withWildcards)
    {
        foreach ((string prefix, bool hasWildcard, bool nameOnly) in scopes)
        {
            // 名字规则（不含 /）没有固定的上级目录：由调用方按「任意层级」处理。
            if (nameOnly)
            {
                continue;
            }

            if (prefix.Length == 0)
            {
                // 规则以通配符开头（如 */cache）：没有明确的上级目录，任何层级都可能命中。
                if (withWildcards)
                {
                    return true;
                }

                continue;
            }

            // 目录在规则目标之上（sub → sub/hidden）：必须进去才能走到目标。
            if (prefix.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 目录在「带通配符的规则」的字面前缀之下（sub/*/hidden 的 sub/xxx）：更深处仍可能命中。
            if (withWildcards
                && hasWildcard
                && normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 路径本身或它的上级目录是否命中某组规则。
    /// <para>
    /// 「包含（或排除）某个文件夹」的意思是连同它下面的全部内容，因此 <c>sub/hidden</c> 这条规则
    /// 也要能命中 <c>sub/hidden/notes.txt</c>；只按整条路径和单个路径段匹配做不到这一点。
    /// </para>
    /// </summary>
    private static bool AnyMatchWithAncestors(
        Regex[] rules, bool hasNestedRule, string normalized, string withSlash, string[] segments)
    {
        if (AnyMatch(rules, normalized, withSlash, segments))
        {
            return true;
        }

        // 规则里没有 /（如 Thumbs.db、*.tmp）：单个路径段已经匹配到任意层级，不必再查上级目录。
        if (!hasNestedRule)
        {
            return false;
        }

        int slash = normalized.LastIndexOf('/');
        while (slash > 0)
        {
            string ancestor = normalized[..slash];
            string[] ancestorSegments = ancestor.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (AnyMatch(rules, ancestor, ancestor + "/", ancestorSegments))
            {
                return true;
            }

            slash = ancestor.LastIndexOf('/');
        }

        return false;
    }

    /// <summary>规则组里是否有带 <c>/</c> 的（按路径写的）规则。</summary>
    private static bool HasNestedRule(IEnumerable<string> patterns) =>
        patterns.Any(static pattern => pattern is not null && Normalize(pattern).Contains('/'));

    /// <summary>把「包含」规则拆成「字面前缀 + 是否还有通配符 + 是否只按名字写」，并去重。</summary>
    private static (string Prefix, bool HasWildcard, bool NameOnly)[] BuildIncludeScopes(IEnumerable<string> patterns)
    {
        var scopes = new List<(string Prefix, bool HasWildcard, bool NameOnly)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string pattern = Normalize(raw.Trim());
            if (pattern.Length == 0)
            {
                continue;
            }

            int wildcard = pattern.IndexOfAny(['*', '?']);
            string prefix = (wildcard < 0 ? pattern : pattern[..wildcard]).TrimEnd('/');
            bool hasWildcard = wildcard >= 0;
            bool nameOnly = !pattern.Contains('/');

            if (seen.Add($"{(hasWildcard ? "*" : "=")}{(nameOnly ? "n" : "p")}{prefix}"))
            {
                scopes.Add((prefix, hasWildcard, nameOnly));
            }
        }

        return [.. scopes];
    }

    private static bool AnyMatch(Regex[] rules, string normalized, string withSlash, string[] segments)
    {
        foreach (Regex rule in rules)
        {
            if (rule.IsMatch(normalized) || rule.IsMatch(withSlash))
            {
                return true;
            }

            foreach (string segment in segments)
            {
                if (rule.IsMatch(segment))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static string Normalize(string path)
    {
        string normalized = path.Replace('\\', '/').Trim('/');

        // 用户常按资源管理器的习惯写成「.\隐藏文件夹」：开头的 ./ 对相对路径没有意义，去掉。
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..].Trim('/');
        }

        return normalized;
    }

    private static Regex[] Compile(IEnumerable<string> patterns)
    {
        var list = new List<Regex>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in patterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string pattern = Normalize(raw.Trim());
            if (pattern.Length == 0 || !seen.Add(pattern))
            {
                continue;
            }

            list.Add(new Regex(
                GlobToRegex(pattern),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));
        }

        return [.. list];
    }

    /// <summary>把 glob 编译为正则表达式（路径已规范化为 <c>/</c> 分隔）。</summary>
    private static string GlobToRegex(string pattern)
    {
        var sb = new StringBuilder("^");

        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];

            if (c == '*')
            {
                bool isDoubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
                if (isDoubleStar)
                {
                    i++;
                    if (i + 1 < pattern.Length && pattern[i + 1] == '/')
                    {
                        i++;
                        sb.Append("(?:.*/)?");   // 任意层级（可为 0 层）
                    }
                    else
                    {
                        sb.Append(".*");
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else if (c == '/')
            {
                sb.Append('/');
            }
            else
            {
                if ("\\^$.|+()[]{}".IndexOf(c) >= 0)
                {
                    sb.Append('\\');
                }

                sb.Append(c);
            }
        }

        sb.Append('$');
        return sb.ToString();
    }
}
