namespace MesApp.Core.Abstractions;

/// <summary>
/// 監査ログ記録サービス（Spec.md 7.6）
/// </summary>
public interface IAuditLogger
{
    /// <param name="detail">
    /// 詳細。変更前後の値のように後から追跡・検索したい情報は匿名オブジェクト等で渡す
    /// （JSONとして保存される）。単なる要約は文字列でよい（そのまま保存される）
    /// </param>
    Task LogAsync(
        string category,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? detail = null,
        CancellationToken ct = default);
}
