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
/// <remarks>
/// 記録日（<c>AuditLog.RecordedOn</c>）は工場のタイムゾーン（<c>BusinessDay:TimeZone</c>）の暦日。
/// サーバーのローカル日付で持つと、UTCのコンテナに置いたときだけ記録日が現場と何時間もずれ、
/// 期間の絞り込みと保持期間の判定が画面の見え方と食い違う。
/// </remarks>
public class AuditLogger(
    MesAppDbContext db,
    IHttpContextAccessor httpContextAccessor,
    IBusinessDateService businessDate) : IAuditLogger
{
    /// <summary>
    /// 日本語をエスケープせずそのまま出力する（監査ログは人が読む前提のため）。
    /// enumも名前で出す：数値のままだと読めないうえ、あとから列挙子を並べ替えると
    /// 過去のログの意味が変わってしまう（DBの他の列も文字列で保存している）。
    /// </summary>
    private static readonly JsonSerializerOptions DetailJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task LogAsync(
        string category,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? detail = null,
        CancellationToken ct = default)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var http = httpContextAccessor.HttpContext;
        var user = http?.User;

        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = timestamp,
            // 製造日（6時境界）ではなく工場ローカルの暦日。監査ログは業務の1日ではなく
            // 法定の保存年数で数えるため、夜勤を前日へ寄せる境界は当てない
            RecordedOn = DateOnly.FromDateTime(businessDate.ToFactoryTime(timestamp).DateTime),
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
