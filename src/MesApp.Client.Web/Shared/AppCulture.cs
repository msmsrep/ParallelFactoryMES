using System.Globalization;
using MesApp.Core.Constants;
using Microsoft.JSInterop;

namespace MesApp.Client.Web.Shared;

/// <summary>
/// 表示言語の読み込み・保存（Spec.md 7.9）。言語はブラウザごとに localStorage へ保存する。
/// 切り替えるのは表示言語（UICulture）だけで、数値・日付の書式（Culture）は変えない。
/// </summary>
public static class AppCulture
{
    /// <summary>
    /// 保存済みの言語を読んで適用する。<c>WebAssemblyHost.RunAsync</c> の前に呼ぶ
    /// （その時点の UICulture に合わせて、訳のサテライトアセンブリが読み込まれる）。
    /// </summary>
    public static async Task ApplyStoredAsync(IJSRuntime js)
    {
        string? stored = null;
        try
        {
            stored = await js.InvokeAsync<string?>("mesApp.culture.get");
        }
        catch (JSException)
        {
            // 更新前の app.js がブラウザに残っているときも、起動を止めず既定の言語で動かす
        }
        var culture = new CultureInfo(MesCultures.Resolve(stored));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        try
        {
            await js.InvokeVoidAsync("mesApp.culture.setDocumentLang", culture.Name);
        }
        catch (JSException)
        {
            // lang 属性は読み上げ等の補助にとどまるため、設定できなくても続ける
        }
    }

    /// <summary>言語を保存する。反映には再読込が要る（文言・サテライトアセンブリは起動時に決まる）</summary>
    public static ValueTask SaveAsync(IJSRuntime js, string name) =>
        js.InvokeVoidAsync("mesApp.culture.set", MesCultures.Resolve(name));
}
