namespace MesApp.Core.Abstractions;

/// <summary>
/// 監査ログ記録サービス（Spec.md 7.6）
/// </summary>
public interface IAuditLogger
{
    Task LogAsync(
        string category,
        string action,
        string? targetType = null,
        string? targetId = null,
        string? detail = null,
        CancellationToken ct = default);
}
