using System.Globalization;
using System.Runtime.CompilerServices;
using MesApp.Core.Constants;

namespace MesApp.Client.Web.Tests;

/// <summary>
/// 表示言語を既定の日本語に固定する（Spec.md 7.9）。固定しないと実行する OS の表示言語で文言が変わり、
/// 日本語の文言を検証するテストが英語の環境（CI の windows-latest など）だけで落ちる。
/// 本番は画面が起動時に（<c>AppCulture</c>）、API は要求ごとに（<c>UseRequestLocalization</c>）言語を決めるので、この問題は起きない
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseDefaultLanguage()
    {
        var culture = new CultureInfo(MesCultures.Default);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
