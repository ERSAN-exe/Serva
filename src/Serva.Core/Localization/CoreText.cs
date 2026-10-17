namespace Serva.Core.Localization;

/// <summary>
/// 核心层的界面文案。
/// <para>
/// <see cref="Serva.Core"/> 不引用 <c>Serva.App</c>：所有面向用户的文本都交给宿主的
/// <see cref="Resolver"/> 决定。未注入时返回英文 —— 与「默认语言 English」一致，
/// 单元测试也就不依赖界面语言。
/// </para>
/// <para>
/// 约定：<see cref="Get(string,string)"/> / <see cref="Format(string,string,object?[])"/>
/// 的第一个参数是英文，第二个是简体中文；占位符统一用 <c>{0}</c>。
/// </para>
/// </summary>
public static class CoreText
{
    /// <summary>文本解析器：(english, chinese) → 当前语言下的文本。由 App 在启动时注入。</summary>
    public static Func<string, string, string> Resolver { get; set; } = static (english, _) => english;

    /// <summary>取当前语言下的文案。</summary>
    public static string Get(string english, string chinese) => Resolver(english, chinese);

    /// <summary>取当前语言下的文案并填充占位符。</summary>
    public static string Format(string english, string chinese, params object?[] args) =>
        string.Format(Resolver(english, chinese), args);
}
