using System.Security.Claims;
using MesApp.Core.Abstractions;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Http;

namespace MesApp.Infrastructure.Services;

/// <summary>
/// 監査ログのDB記録実装。HTTPコンテキストから操作ユーザー・IPアドレスを自動取得する。
/// </summary>
public class AuditLogger(MesAppDbContext db, IHttpContextAccessor httpContextAccessor) : IAuditLogger
{
    public async Task LogAsync(
        string category,
        string action,
        string? targetType = null,
        string? targetId = null,
        string? detail = null,
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
            Detail = detail,
            IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
        });
        await db.SaveChangesAsync(ct);
    }
}
