using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Planning;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 生産計画（Spec.md 5.2 ProductionPlan。A-30-10-01 生産進捗管理モニタリングの「予」）。
/// 製造日×品目×工程（作業区は任意）の計画数量を受け取る口。MESは計画を立てない（A-10 は対象外）。
/// 製造指図・作業指示・実績からは参照させない（計画 → 既存実績の一方向の依存）。
/// </summary>
[ApiController]
[Route("api/production-plans")]
[Authorize]
public class ProductionPlansController(
    MesAppDbContext db, IAuditLogger auditLogger, ProductionPlanService planService, IBusinessDateService businessDate)
    : ControllerBase
{
    /// <summary>
    /// 計画の一覧。期間は製造日の両端を含む。作業区に上位の段を指定したら配下へ展開して返す
    /// （ラインを指定したときに、配下の作業区の計画も並ぶようにする）
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProductionPlanResponse>>> List(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int? productId = null, [FromQuery] int? processId = null,
        [FromQuery] int? workCenterId = null, CancellationToken ct = default)
    {
        var (plans, error) = await SearchAsync(from, to, productId, processId, workCenterId, ct);
        if (error is not null)
        {
            return error;
        }
        return plans!.Select(ToResponse).ToList();
    }

    /// <summary>
    /// 一覧と同じ条件で絞った計画のCSV出力（Spec.md 3.8）。列はCSV取込（<c>api/masters/csv/production-plans</c>）と同じで、
    /// そのまま直して取り込み直せる。計画は日々増えるので、マスタのように全件を出さず画面の絞り込みに合わせる
    /// </summary>
    [HttpGet("csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int? productId = null, [FromQuery] int? processId = null,
        [FromQuery] int? workCenterId = null, CancellationToken ct = default)
    {
        var (plans, error) = await SearchAsync(from, to, productId, processId, workCenterId, ct);
        if (error is not null)
        {
            return error;
        }
        var csv = MasterCsvService.FormatProductionPlans(plans!);
        return File(CsvFile.ToUtf8Bom(csv), "text/csv; charset=utf-8", $"production-plans_{DateTime.Now:yyyyMMdd}.csv");
    }

    /// <summary>
    /// 工程別の予実（製造日×品目×工程で1行）。期間は製造日の from〜to（省略時は当日の製造日）。
    /// 集計の規則は <see cref="ProductionPlanService"/>
    /// </summary>
    [HttpGet("plan-actual")]
    public async Task<ActionResult<List<ProductionPlanActualRow>>> PlanActual(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        [FromQuery] int? productId = null, [FromQuery] int? processId = null,
        [FromQuery] int? workCenterId = null, CancellationToken ct = default)
    {
        var filter = new ProductionPlanService.Filter(
            from ?? businessDate.Today, to ?? businessDate.Today, productId, processId, workCenterId);
        if (filter.From > filter.To)
        {
            return this.BadRequestProblem(ApiText.T("期間の開始日が終了日より後になっています。"));
        }
        if (workCenterId is { } wcId && !await db.WorkCenters.AnyAsync(w => w.Id == wcId, ct))
        {
            return this.BadRequestProblem(ApiText.T("作業区（ID {0}）が見つかりません。", wcId));
        }
        return await planService.GetPlanActualAsync(filter, ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductionPlanResponse>> Get(int id, CancellationToken ct)
    {
        var plan = await BaseQuery().FirstOrDefaultAsync(p => p.Id == id, ct);
        return plan is null ? NotFound() : ToResponse(plan);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ProductionPlanResponse>> Create(ProductionPlanRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(request, null, ct) is { } error)
        {
            return error;
        }

        var plan = new ProductionPlan
        {
            BusinessDate = request.BusinessDate,
            ProductId = request.ProductId,
            ProcessId = request.ProcessId,
            WorkCenterId = request.WorkCenterId,
            PlannedQuantity = request.PlannedQuantity,
            Note = request.Note,
        };
        db.ProductionPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Planning", "Create", nameof(ProductionPlan), plan.Id.ToString(),
            detail: Summary(plan), ct: ct);
        var created = await BaseQuery().FirstAsync(p => p.Id == plan.Id, ct);
        return CreatedAtAction(nameof(Get), new { id = plan.Id }, ToResponse(created));
    }

    /// <summary>計画は改訂されるので、キーの付け替えも含めて更新できる。変更前後を監査ログに残す</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<ProductionPlanResponse>> Update(
        int id, ProductionPlanRequest request, CancellationToken ct)
    {
        var plan = await db.ProductionPlans.FindAsync([id], ct);
        if (plan is null)
        {
            return NotFound();
        }
        if (await ValidateAsync(request, id, ct) is { } error)
        {
            return error;
        }

        var before = Snapshot(plan);
        plan.BusinessDate = request.BusinessDate;
        plan.ProductId = request.ProductId;
        plan.ProcessId = request.ProcessId;
        plan.WorkCenterId = request.WorkCenterId;
        plan.PlannedQuantity = request.PlannedQuantity;
        plan.Note = request.Note;
        plan.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Planning", "Update", nameof(ProductionPlan), id.ToString(),
            detail: new { before, after = Snapshot(plan), reason = (string?)null }, ct: ct);
        return ToResponse(await BaseQuery().FirstAsync(p => p.Id == id, ct));
    }

    /// <summary>計画は業務の記録ではなく受け取った予定なので、無効化ではなく削除する</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var plan = await db.ProductionPlans.FindAsync([id], ct);
        if (plan is null)
        {
            return NotFound();
        }
        var summary = Summary(plan);
        db.ProductionPlans.Remove(plan);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Planning", "Delete", nameof(ProductionPlan), id.ToString(),
            detail: summary, ct: ct);
        return NoContent();
    }

    /// <summary>判定は CSV取込と共通（<see cref="ProductionPlanPolicy"/>）。ここでは結果を ProblemDetails にするだけ</summary>
    private async Task<ActionResult?> ValidateAsync(ProductionPlanRequest request, int? excludeId, CancellationToken ct)
    {
        var key = new ProductionPlanPolicy.PlanKey(
            request.BusinessDate, request.ProductId, request.ProcessId, request.WorkCenterId);
        return await ProductionPlanPolicy.CheckAsync(db, key, request.PlannedQuantity, excludeId, ct) switch
        {
            null => null,
            { Conflict: true } v => this.ConflictProblem(v.Message),
            var v => this.BadRequestProblem(v.Message),
        };
    }

    /// <summary>一覧・CSV出力の共通の絞り込み（製造日・品目・工程・作業区の配下）。並びは製造日・品目・工程・作業区のコード順</summary>
    private async Task<(List<ProductionPlan>? Plans, ActionResult? Error)> SearchAsync(
        DateOnly? from, DateOnly? to, int? productId, int? processId, int? workCenterId, CancellationToken ct)
    {
        if (from > to)
        {
            return (null, this.BadRequestProblem(ApiText.T("期間の開始日が終了日より後になっています。")));
        }

        var query = BaseQuery();
        if (from is { } f)
        {
            query = query.Where(p => p.BusinessDate >= f);
        }
        if (to is { } t)
        {
            query = query.Where(p => p.BusinessDate <= t);
        }
        if (productId is { } pid)
        {
            query = query.Where(p => p.ProductId == pid);
        }
        if (processId is { } prid)
        {
            query = query.Where(p => p.ProcessId == prid);
        }
        if (workCenterId is { } rootId)
        {
            var all = await db.WorkCenters.AsNoTracking().ToListAsync(ct);
            if (all.All(x => x.Id != rootId))
            {
                return (null, this.BadRequestProblem(ApiText.T("作業区（ID {0}）が見つかりません。", rootId)));
            }
            var targets = WorkCenterHierarchyPolicy.SelfAndDescendantIds(rootId, all);
            query = query.Where(p => p.WorkCenterId != null && targets.Contains(p.WorkCenterId.Value));
        }

        var plans = await query.ToListAsync(ct);
        return ([.. plans
            .OrderBy(p => p.BusinessDate)
            .ThenBy(p => p.Product!.Code, StringComparer.Ordinal)
            .ThenBy(p => p.Process!.Code, StringComparer.Ordinal)
            .ThenBy(p => p.WorkCenter?.Code, StringComparer.Ordinal)], null);
    }

    private IQueryable<ProductionPlan> BaseQuery() =>
        db.ProductionPlans.AsNoTracking()
            .Include(p => p.Product)
            .Include(p => p.Process)
            .Include(p => p.WorkCenter);

    private static object Snapshot(ProductionPlan p) => new
    {
        p.BusinessDate, p.ProductId, p.ProcessId, p.WorkCenterId, p.PlannedQuantity, p.Note,
    };

    private static string Summary(ProductionPlan p) =>
        $"date={p.BusinessDate:yyyy-MM-dd} product={p.ProductId} process={p.ProcessId} "
        + $"workCenter={p.WorkCenterId?.ToString() ?? "-"} qty={p.PlannedQuantity}";

    private static ProductionPlanResponse ToResponse(ProductionPlan p) =>
        new(p.Id, p.BusinessDate,
            p.ProductId, p.Product!.Code, p.Product.Name,
            p.ProcessId, p.Process!.Code, p.Process.Name,
            p.WorkCenterId, p.WorkCenter?.Code, p.WorkCenter?.Name,
            p.PlannedQuantity, p.Note, p.UpdatedAt);
}
