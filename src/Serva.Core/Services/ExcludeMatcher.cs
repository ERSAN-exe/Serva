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
    /// 内置的「包含」规则默认值（对应 <see cref="Models.SyncOptions.AlwaysIncludePatterns"/>）：
    /// 以点开头、平时会被隐藏规则跳过，但确实是用户要同步的内容（Minecraft 的游戏目录）。
    /// <para>设置页里这条规则<strong>可以删除</strong>，删掉后不再自动补回。</para>
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultIncludedPatterns = [".minecraft"];

    private readonly Regex[] _exclude;
    private readonly Regex[] _include;
    private readonly Regex[] _alwaysIncluded;

    public ExcludeMatcher(
        IEnumerable<string>? excludePatterns = null,
        IEnumerable<string>? includePatterns = null,
        IEnumerable<string>? alwaysIncludePatterns = null)
    {
        _exclude = Compile(excludePatterns ?? DefaultExcludePatterns);
        _include = Compile(includePatterns ?? []);
        _alwaysIncluded = Compile(alwaysIncludePatterns ?? DefaultIncludedPatterns);
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

        // 「包含的文件」优先：例如默认的 .minecraft —— 用户加进来就是明确要它，不能被排除规则挡掉。
        if (AnyMatch(_alwaysIncluded, normalized, withSlash, segments))
        {
            return false;
        }

        if (_include.Length > 0 && !AnyMatch(_include, normalized, withSlash, segments))
        {
            return true;
        }

        return AnyMatch(_exclude, normalized, withSlash, segments);
    }

    /// <summary>
    /// 该相对路径是否被<strong>显式包含</strong>：命中用户的「仅包含」规则，
    /// 或命中内置的「隐藏也照扫」清单（如 <c>.minecraft</c>）。
    /// <para>
    /// 扫描器用它决定「隐藏 / 系统文件夹」要不要跳过 ——
    /// 用户特意把某个隐藏文件夹加成仅包含时，就不是要忽略它，而是只要它。
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

        return (_include.Length > 0 && AnyMatch(_include, normalized, withSlash, segments))
               || AnyMatch(_alwaysIncluded, normalized, withSlash, segments);
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

    internal static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

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
