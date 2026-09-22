using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Audit;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 監査ログの参照（Spec.md 7.6）。記録するだけでは「追跡可能にする」を満たさないため、
/// 誰が・いつ・何を変更したかをアプリから引けるようにする。
/// <para>
/// <b>個々の記録は書き換えられない</b>：作成・更新・1件削除のエンドポイントは置かない
/// （記録は <c>IAuditLogger</c> 経由でのみ増える）。唯一の削除は保持期間を過ぎた分の
/// 一括削除（<see cref="Purge"/>）で、これも削除したこと自体が監査ログに残る。
/// システム管理者専用（Spec.md 7.4）。
/// </para>
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = MesRoleGroups.UserAdmin)]
public class AuditLogsController(
    MesAppDbContext db,
    IAuditLogger auditLogger,
    IConfiguration configuration,
    IBusinessDateService businessDate) : ControllerBase
{
    /// <summary>保持期間の既定（年）。Spec.md 7.6</summary>
    private const int DefaultRetentionYears = 5;

    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditLogResponse>>> List(
        [FromQuery] PageQuery paging, [FromQuery] AuditLogQuery filter, CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking();

        // 期間は RecordedOn（記録日・工場のローカル暦日）で絞る。画面の表示もローカル時刻。
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

    /// <summary>
    /// 保持期間を過ぎた監査ログの一括削除（Spec.md 7.6）。指定日を含めてそれ以前を削除する。
    /// <c>?dryRun=true</c> で件数だけ確認できる（DBには触らない）。
    /// </summary>
    /// <remarks>
    /// <b>保持期間の内側は削除できない</b>：任意の期間を消せると、直前の操作の記録を消して
    /// 隠せてしまい、監査ログが証跡として成り立たなくなる。保持期間より古い分の整理だけを許す。
    /// 削除したこと自体（期間・件数・理由・実行者）は監査ログに残る。
    /// </remarks>
    [HttpPost("purge")]
    public async Task<ActionResult<AuditLogPurgeResult>> Purge(
        AuditLogPurgeRequest request, [FromQuery] bool dryRun = false, CancellationToken ct = default)
    {
        var cutoff = RetentionCutoff();
        if (request.To > cutoff)
        {
            return this.BadRequestProblem(
                ApiText.T("保持期間内の監査ログは削除できません（{0:yyyy-MM-dd} 以前が対象です）。", cutoff));
        }

        var target = db.AuditLogs.Where(a => a.RecordedOn <= request.To);
        var count = await target.CountAsync(ct);
        if (dryRun)
        {
            return new AuditLogPurgeResult(request.To, count, cutoff, DryRun: true);
        }

        await target.ExecuteDeleteAsync(ct);
        // 記録は削除のあとに残す（同じ削除で消えないように）
        await auditLogger.LogAsync("Audit", "Purge", nameof(AuditLog), null,
            detail: new { to = request.To, deleted = count, retentionCutoff = cutoff, reason = request.Reason },
            ct: ct);
        return new AuditLogPurgeResult(request.To, count, cutoff, DryRun: false);
    }

    /// <summary>この日以前なら削除してよい、という境界（今日から保持期間ぶん遡った日の前日）</summary>
    private DateOnly RetentionCutoff()
    {
        var years = configuration.GetValue("Audit:RetentionYears", DefaultRetentionYears);
        // 記録日は工場のローカル暦日なので、境界も同じ基準で求める。
        // DateTime.Now（サーバーのローカル）で求めると、UTCのコンテナに置いたときに
        // 境界だけ現場とずれ、消せるはずの日／消せない日が1日食い違う
        var today = DateOnly.FromDateTime(businessDate.ToFactoryTime(DateTimeOffset.UtcNow).DateTime);
        return today.AddYears(-years).AddDays(-1);
    }
}
