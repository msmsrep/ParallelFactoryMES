using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MesApp.Core.Abstractions;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Http;

namespace MesApp.Infrastructure.Services;

/// <summary>
/// 監査ログのDB記録実装。HTTPコンテキストから操作ユーザー・IPアドレスを自動取得する。
/// </summary>
public class AuditLogger(MesAppDbContext db, IHttpContextAccessor httpContextAccessor) : IAuditLogger
{
    /// <summary>日本語をエスケープせずそのまま出力する（監査ログは人が読む前提のため）</summary>
    private static readonly JsonSerializerOptions DetailJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task LogAsync(
        string category,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? detail = null,
        CancellationToken ct = default)
    {
        var http = httpContextAccessor.HttpContext;
        var user = http?.User;

        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = DateTimeOffset.UtcNow,
            UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
            UserName = user?.Identity?.Name,
            Category = category,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = Serialize(detail),
            IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
        });

        // 監査ログは業務データの保存後に呼ばれる。ここで ct を尊重すると、
        // 利用者が画面を閉じた瞬間などに「業務データは確定したのに記録が残らない」ことが起きるため、
        // 保存だけはキャンセルさせない（Spec.md 7.6：書き込み系は必ず記録する）。
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>文字列はそのまま、それ以外はJSONとして保存する</summary>
    private static string? Serialize(object? detail) => detail switch
    {
        null => null,
        string text => text,
        _ => JsonSerializer.Serialize(detail, DetailJsonOptions),
    };
}
