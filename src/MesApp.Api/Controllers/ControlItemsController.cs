using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 工程管理項目マスタ（Spec.md 5.1 ControlItem。B-30-30-04）。
/// 温度・回転数など製造時に記録すべき条件の定義と指示値・上下限を持つ。
/// 条件の改訂では版数を自動インクリメントする（検査項目 C-10-10-03 と同じ扱い）。
/// </summary>
[ApiController]
[Route("api/control-items")]
[Authorize]
public class ControlItemsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ControlItemResponse>>> List(
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
    public async Task<ActionResult<ControlItemResponse>> Get(int id, CancellationToken ct)
    {
        var item = await BaseQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
        return item is null ? NotFound() : ToResponse(item);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ControlItemResponse>> Create(ControlItemRequest request, CancellationToken ct)
    {
        if (await db.ControlItems.AnyAsync(i => i.Code == request.Code, ct))
        {
            return this.ConflictProblem(ApiText.T("工程管理項目コード '{0}' は既に存在します。", request.Code));
        }
        if (await ValidateAsync(request, ct) is { } error)
        {
            return this.BadRequestProblem(error);
        }

        var item = new ControlItem
        {
            Code = request.Code,
            Name = request.Name,
            Unit = request.Unit,
            TargetProductId = request.TargetProductId,
            TargetProcessId = request.TargetProcessId,
            TargetValue = request.TargetValue,
            LowerLimit = request.LowerLimit,
            UpperLimit = request.UpperLimit,
        };
        db.ControlItems.Add(item);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(ControlItem), item.Id.ToString(),
            detail: $"code={item.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = item.Id }, (await GetResponseAsync(item.Id, ct))!);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ControlItemResponse>> Update(
        int id, ControlItemRequest request, CancellationToken ct)
    {
        var item = await db.ControlItems.FindAsync([id], ct);
        if (item is null)
        {
            return NotFound();
        }
        if (await db.ControlItems.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return this.ConflictProblem(ApiText.T("工程管理項目コード '{0}' は既に存在します。", request.Code));
        }
        if (await ValidateAsync(request, ct) is { } error)
        {
            return this.BadRequestProblem(error);
        }

        item.Code = request.Code;
        item.Name = request.Name;
        item.Unit = request.Unit;
        item.TargetProductId = request.TargetProductId;
        item.TargetProcessId = request.TargetProcessId;
        item.TargetValue = request.TargetValue;
        item.LowerLimit = request.LowerLimit;
        item.UpperLimit = request.UpperLimit;
        item.Version++; // 条件の改訂
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(ControlItem), id.ToString(),
            detail: $"code={item.Code}, version={item.Version}", ct: ct);
        return (await GetResponseAsync(id, ct))!;
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public Task<IActionResult> Deactivate(int id, CancellationToken ct) =>
        this.DeactivateMasterAsync<ControlItem>(db, auditLogger, id, item => $"code={item.Code}", ct);

    /// <summary>上下限の整合と参照先の存在を確認する。問題があれば日本語の理由を返す</summary>
    private async Task<string?> ValidateAsync(ControlItemRequest request, CancellationToken ct)
    {
        if (request.LowerLimit is { } lower && request.UpperLimit is { } upper && lower > upper)
        {
            return ApiText.T("許容下限は許容上限以下で指定してください。");
        }
        // 指示値が許容範囲の外にあると、指示どおりに作っても逸脱と判定されてしまう
        if (request.TargetValue is { } target)
        {
            if (request.LowerLimit is { } l && target < l)
            {
                return ApiText.T("指示値が許容下限を下回っています。");
            }
            if (request.UpperLimit is { } u && target > u)
            {
                return ApiText.T("指示値が許容上限を超えています。");
            }
        }
        if (request.TargetProductId is { } productId
            && !await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            return ApiText.T("対象品目（ID {0}）が見つかりません。", productId);
        }
        if (request.TargetProcessId is { } processId
            && !await db.Processes.AnyAsync(p => p.Id == processId, ct))
        {
            return ApiText.T("対象工程（ID {0}）が見つかりません。", processId);
        }
        return null;
    }

    private IQueryable<ControlItem> BaseQuery() =>
        db.ControlItems.AsNoTracking()
            .Include(i => i.TargetProduct)
            .Include(i => i.TargetProcess);

    private async Task<ControlItemResponse?> GetResponseAsync(int id, CancellationToken ct)
    {
        var item = await BaseQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
        return item is null ? null : ToResponse(item);
    }

    private static ControlItemResponse ToResponse(ControlItem i) =>
        new(i.Id, i.Code, i.Name, i.Unit,
            i.TargetProductId, i.TargetProduct?.Code,
            i.TargetProcessId, i.TargetProcess?.Code,
            i.TargetValue, i.LowerLimit, i.UpperLimit, i.Version, i.IsActive);
}
