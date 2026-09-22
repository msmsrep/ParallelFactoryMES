using System.Globalization;
using System.Resources;

namespace MesApp.Api.Localization;

/// <summary>
/// API が返す文言の翻訳（Spec.md 7.9 多言語対応）。日本語の原文をキーにし、英語の訳を <c>ApiText.en.resx</c> に持つ。
/// <c>ApiText.resx</c>（既定）は空のままにする。訳の無いキーは原文（日本語）がそのまま返る。
/// <para>
/// 利用者に返す日本語の文言は、Controller・Policy・Service のどこで作る場合も必ず <see cref="T(string, object?[])"/> で包む。
/// 値を埋め込む文言は補間文字列にせず <c>{0}</c> の書式と引数で渡す（補間すると値ごとに原文が変わり、訳のキーに当たらない）。
/// 表示言語は要求の <c>Accept-Language</c> で決まる（<c>UseRequestLocalization</c> が UICulture を設定する）。
/// </para>
/// <para>
/// <b>保存される文字列は包まない</b>：在庫トランザクションの備考、ロット・作業指示の状態履歴の理由、監査ログの detail、
/// 自動作成する記録の本文（不適合報告・リワーク指図の備考など）。表示言語によって記録の中身が変わってしまうため。
/// </para>
/// </summary>
public static class ApiText
{
    private static readonly ResourceManager Resources = new(typeof(ApiText));

    /// <summary>原文を現在の表示言語へ訳す。値は <c>{0}</c> の位置へ埋め込む（書式は数値・日付の Culture に従う）</summary>
    public static string T(string original, params object?[] args)
    {
        var text = Lookup(original);
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }

    private static string Lookup(string original)
    {
        try
        {
            return Resources.GetString(original, CultureInfo.CurrentUICulture) ?? original;
        }
        catch (MissingManifestResourceException)
        {
            return original;
        }
    }
}
