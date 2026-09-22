namespace MesApp.Api.Localization;

/// <summary>
/// API が返す文言のリソース（Spec.md 7.9 多言語対応）。日本語の原文をキーにし、英語の訳を <c>ApiText.en.resx</c> に持つ。
/// <c>ApiText.resx</c>（既定）は空のままにする。訳の無いキーは原文（日本語）がそのまま返る。
/// </summary>
public sealed class ApiText;
