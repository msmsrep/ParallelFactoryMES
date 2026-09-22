namespace MesApp.Core.Constants;

/// <summary>
/// 表示言語（Spec.md 7.9 多言語対応）。APIの<c>Accept-Language</c>の受け付けと画面の言語切替で同じ定義を使う。
/// 文言は日本語の原文をキーにし、英語の訳だけを <c>*.en.resx</c> に持つ（訳が無ければ日本語のまま出る）。
/// </summary>
public static class MesCultures
{
    public const string Japanese = "ja";
    public const string English = "en";

    /// <summary>既定の表示言語（原文の言語）</summary>
    public const string Default = Japanese;

    public static readonly IReadOnlyList<string> All = [Japanese, English];

    /// <summary>対応している言語ならその名前を、そうでなければ既定の言語を返す（<c>en-US</c> は <c>en</c> に寄せる）</summary>
    public static string Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Default;
        }
        var language = name.Split('-')[0];
        return All.FirstOrDefault(c => c.Equals(language, StringComparison.OrdinalIgnoreCase)) ?? Default;
    }
}
