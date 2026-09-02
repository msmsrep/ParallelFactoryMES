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
            return Conflict(new ProblemDetails { Title = $"検査項目コード '{request.Code}' は既に存在します。" });
        }
        var error = await ValidateTargetsAsync(request, ct);
        if (error is not null)
        {
            return BadRequest(new ProblemDetails { Title = error });
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
            return Conflict(new ProblemDetails { Title = $"検査項目コード '{request.Code}' は既に存在します。" });
        }
        var error = await ValidateTargetsAsync(request, ct);
        if (error is not null)
        {
            return BadRequest(new ProblemDetails { Title = error });
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
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var i = await db.InspectionItems.FindAsync([id], ct);
        if (i is null)
        {
            return NotFound();
        }
        i.IsActive = false;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(InspectionItem), id.ToString(),
            detail: $"code={i.Code}", ct: ct);
        return NoContent();
    }

    private async Task<string?> ValidateTargetsAsync(InspectionItemRequest request, CancellationToken ct)
    {
        if (request.TargetProductId is int productId && !await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            return "存在しない対象品目IDです。";
        }
        if (request.TargetProcessId is int processId && !await db.Processes.AnyAsync(p => p.Id == processId, ct))
        {
            return "存在しない対象工程IDです。";
        }
        if (request.LowerLimit is not null && request.UpperLimit is not null && request.LowerLimit > request.UpperLimit)
        {
            return "規格値の下限が上限を超えています。";
        }
        return null;
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
