using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 検査項目・基準マスタ（Spec.md 5.1 InspectionItem。C-10-10）。
/// 基準の更新（C-10-10-03）では版数を自動インクリメントする。
/// 対象品目・対象工程の意味と組み合わせの制約は <see cref="InspectionItemPolicy"/>。
/// </summary>
[ApiController]
[Route("api/inspection-items")]
[Authorize]
public class InspectionItemsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<InspectionItemResponse>>> List(
        [FromQuery] bool includeInactive = false,
        [FromQuery] int? targetProductId = null,
        [FromQuery] int? targetProcessId = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (!includeInactive)
        {
            query = query.Where(i => i.IsActive);
        }
        if (targetProductId is not null)
        {
            query = query.Where(i => i.TargetProductId == targetProductId);
        }
        if (targetProcessId is not null)
        {
            query = query.Where(i => i.TargetProcessId == targetProcessId);
        }
        return await query.OrderBy(i => i.Code).Select(i => ToResponse(i)).ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InspectionItemResponse>> Get(int id, CancellationToken ct)
    {
        var item = await BaseQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
        return item is null ? NotFound() : ToResponse(item);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<InspectionItemResponse>> Create(InspectionItemRequest request, CancellationToken ct)
    {
        if (await db.InspectionItems.AnyAsync(i => i.Code == request.Code, ct))
        {
            return this.ConflictProblem(ApiText.T("検査項目コード '{0}' は既に存在します。", request.Code));
        }
        var error = await ValidateTargetsAsync(request, ct);
        if (error is not null)
        {
            return this.BadRequestProblem(error);
        }

        var i = new InspectionItem
        {
            Code = request.Code,
            Name = request.Name,
            TargetProductId = request.TargetProductId,
            TargetProcessId = request.TargetProcessId,
            Type = request.Type,
            LowerLimit = request.LowerLimit,
            UpperLimit = request.UpperLimit,
            StandardValue = request.StandardValue,
            Method = request.Method,
            SamplingCount = request.SamplingCount,
        };
        db.InspectionItems.Add(i);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(InspectionItem), i.Id.ToString(),
            detail: $"code={i.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = i.Id },
            await GetResponseAsync(i.Id, ct));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<InspectionItemResponse>> Update(int id, InspectionItemRequest request, CancellationToken ct)
    {
        var i = await db.InspectionItems.FindAsync([id], ct);
        if (i is null)
        {
            return NotFound();
        }
        if (await db.InspectionItems.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return this.ConflictProblem(ApiText.T("検査項目コード '{0}' は既に存在します。", request.Code));
        }
        var error = await ValidateTargetsAsync(request, ct);
        if (error is not null)
        {
            return this.BadRequestProblem(error);
        }

        i.Code = request.Code;
        i.Name = request.Name;
        i.TargetProductId = request.TargetProductId;
        i.TargetProcessId = request.TargetProcessId;
        i.Type = request.Type;
        i.LowerLimit = request.LowerLimit;
        i.UpperLimit = request.UpperLimit;
        i.StandardValue = request.StandardValue;
        i.Method = request.Method;
        i.SamplingCount = request.SamplingCount;
        i.Version++; // 基準改訂（C-10-10-03）
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(InspectionItem), id.ToString(),
            detail: $"code={i.Code}, version={i.Version}", ct: ct);
        return await GetResponseAsync(id, ct);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public Task<IActionResult> Deactivate(int id, CancellationToken ct) =>
        this.DeactivateMasterAsync<InspectionItem>(db, auditLogger, id, i => $"code={i.Code}", ct);

    private async Task<string?> ValidateTargetsAsync(InspectionItemRequest request, CancellationToken ct)
    {
        if (request.TargetProductId is int productId && !await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            return ApiText.T("存在しない対象品目IDです。");
        }
        if (request.TargetProcessId is int processId && !await db.Processes.AnyAsync(p => p.Id == processId, ct))
        {
            return ApiText.T("存在しない対象工程IDです。");
        }
        // 判定条件はマスタCSV取込と共通（片方だけ通る状態を作らない。Spec.md 7.4）
        return InspectionItemPolicy.CheckDefinition(request.Type, request.TargetProductId, request.TargetProcessId,
            request.LowerLimit, request.UpperLimit, request.StandardValue);
    }

    /// <summary>
    /// 対象品目・対象工程を読み込んだ検索元。応答にコードを載せることで、
    /// 画面がコードを出すためにマスタを全件持たなくてよくなる
    /// （品目は件数が有界でなく、全件取得できない：Spec.md 7.5）
    /// </summary>
    private IQueryable<InspectionItem> BaseQuery() =>
        db.InspectionItems.AsNoTracking()
            .Include(i => i.TargetProduct)
            .Include(i => i.TargetProcess);

    /// <summary>保存後の応答。対象マスタを読み込み直してコードまで返す</summary>
    private async Task<InspectionItemResponse> GetResponseAsync(int id, CancellationToken ct) =>
        ToResponse(await BaseQuery().FirstAsync(i => i.Id == id, ct));

    private static InspectionItemResponse ToResponse(InspectionItem i) =>
        new(i.Id, i.Code, i.Name,
            i.TargetProductId, i.TargetProduct?.Code,
            i.TargetProcessId, i.TargetProcess?.Code,
            i.Type, i.LowerLimit, i.UpperLimit, i.StandardValue,
            i.Method, i.SamplingCount, i.Version, i.IsActive);
}
