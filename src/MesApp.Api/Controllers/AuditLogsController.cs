using MesApp.Core.Contracts.Audit;
using MesApp.Core.Contracts.Common;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 監査ログの参照（Spec.md 7.6）。記録するだけでは「追跡可能にする」を満たさないため、
/// 誰が・いつ・何を変更したかをアプリから引けるようにする。
/// <para>
/// <b>参照専用</b>：監査ログを書き換えられると記録の意味がなくなるので、
/// 作成・更新・削除のエンドポイントは置かない（記録は <c>IAuditLogger</c> 経由でのみ増える）。
/// システム管理者専用（Spec.md 7.4）。
/// </para>
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = MesRoleGroups.UserAdmin)]
public class AuditLogsController(MesAppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogResponse>>> List(
        [FromQuery] PageQuery paging, [FromQuery] AuditLogQuery filter, CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking();

        // 期間は RecordedOn（記録日・サーバーのローカル日付）で絞る。画面の表示もローカル時刻。
        // SQLiteは DateTimeOffset の比較をSQLへ変換できず、Timestamp では絞り込めない
        if (filter.From is DateOnly from)
        {
            query = query.Where(a => a.RecordedOn >= from);
        }
        if (filter.To is DateOnly to)
        {
            // 指定日を含める
            query = query.Where(a => a.RecordedOn <= to);
        }
        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            query = query.Where(a => a.Category == filter.Category);
        }
        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(a => a.Action == filter.Action);
        }
        if (!string.IsNullOrWhiteSpace(filter.TargetType))
        {
            query = query.Where(a => a.TargetType == filter.TargetType);
        }
        if (!string.IsNullOrWhiteSpace(filter.TargetId))
        {
            query = query.Where(a => a.TargetId == filter.TargetId);
        }
        if (!string.IsNullOrWhiteSpace(filter.UserId))
        {
            query = query.Where(a => a.UserId == filter.UserId);
        }

        // 新しい順。SQLiteはDateTimeOffsetの並べ替えができないため、記録順＝時刻順である
        // Idの降順で代用する（採番順に追記されるだけで、あとから挿入されることはない）。
        // ページ間の重複・欠落も起きない（Spec.md 7.5：並べ替えを確定させる）
        return await query
            .OrderByDescending(a => a.Id)
            .Select(a => new AuditLogResponse(
                a.Id, a.Timestamp, a.UserId, a.UserName, a.Category, a.Action,
                a.TargetType, a.TargetId, a.Detail, a.IpAddress))
            .ToPagedResultAsync(paging, ct);
    }

    /// <summary>絞り込みに使う分類・操作の一覧（画面のドロップダウン用。実際に記録がある値だけを返す）</summary>
    [HttpGet("categories")]
    public async Task<ActionResult<List<AuditCategoryOption>>> Categories(CancellationToken ct = default) =>
        await db.AuditLogs.AsNoTracking()
            .GroupBy(a => new { a.Category, a.Action })
            // 並べ替えは射影の前に置く（射影後のレコードに対する OrderBy はSQLへ変換できない）
            .OrderBy(g => g.Key.Category).ThenBy(g => g.Key.Action)
            .Select(g => new AuditCategoryOption(g.Key.Category, g.Key.Action, g.Count()))
            .ToListAsync(ct);
}
