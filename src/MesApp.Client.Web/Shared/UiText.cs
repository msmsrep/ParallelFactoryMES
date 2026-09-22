namespace MesApp.Client.Web.Shared;

/// <summary>
/// 画面の文言のリソース（Spec.md 7.9 多言語対応）。全画面に <c>L</c> として注入する（<c>_Imports.razor</c>）。
/// 日本語の原文をキーにし（<c>@L["ログアウト"]</c>）、英語の訳を <c>UiText.en.resx</c> に持つ。
/// <c>UiText.resx</c>（既定）は空のままにする。訳の無いキーは原文（日本語）がそのまま出る。
/// </summary>
public sealed class UiText;
