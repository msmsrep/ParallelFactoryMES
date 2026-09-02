using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 品目マスタ＋MBOM＋工順/BOP（Spec.md 3.1：A-40-10、A-40-20）。
/// 参照は認証済み全員、更新はマスタ管理ロールのみ。削除は論理削除（IsActive=false）。
/// </summary>
[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ProductResponse>>> List(
        [FromQuery] bool includeInactive = false, [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var query = db.Products.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => p.Code.Contains(search) || p.Name.Contains(search));
        }
        return await query.OrderBy(p => p.Code).Select(p => ToResponse(p)).ToListAsync(ct);
    }

    /// <summary>
    /// 品目の選択肢（Spec.md 7.5）。品目は他のマスタと違って件数が有界とは言えず、
    /// 全画面のドロップダウンで全件を読むと初期表示が重くなるため、検索付きの選択肢APIを分ける
    /// </summary>
    [HttpGet("options")]
    public async Task<ActionResult<OptionsResult<ProductResponse>>> Options(
        [FromQuery] OptionQuery options, CancellationToken ct = default)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (options.Keyword is { } keyword)
        {
            query = query.Where(p => p.Code.Contains(keyword) || p.Name.Contains(keyword));
        }
        return await query.OrderBy(p => p.Code).Select(p => ToResponse(p))
            .ToOptionsResultAsync(options, ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductResponse>> Get(int id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? NotFound() : ToResponse(product);
    }

    [HttpPost]
    [Authorize(Roles = RoleGroups.MasterWrite)]
    public async Task<ActionResult<ProductResponse>> Create(ProductRequest request, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Code == request.Code, ct))
        {
            return Conflict(new ProblemDetails { Title = $"品目コード '{request.Code}' は既に存在します。" });
        }

        var product = new Product
        {
            Code = request.Code,
            Name = request.Name,
            Unit = request.Unit,
            Specification = request.Specification,
            Type = request.Type,
            StandardDefectRate = request.StandardDefectRate,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Product), product.Id.ToString(),
            detail: $"code={product.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, ToResponse(product));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = RoleGroups.MasterWrite)]
    public async Task<ActionResult<ProductResponse>> Update(int id, ProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null)
        {
            return NotFound();
        }
        if (await db.Products.AnyAsync(p => p.Code == request.Code && p.Id != id, ct))
        {
            return Conflict(new ProblemDetails { Title = $"品目コード '{request.Code}' は既に存在します。" });
        }

        product.Code = request.Code;
        product.Name = request.Name;
        product.Unit = request.Unit;
        product.Specification = request.Specification;
        product.Type = request.Type;
        product.StandardDefectRate = request.StandardDefectRate;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Product), id.ToString(),
            detail: $"code={product.Code}", ct: ct);
        return ToResponse(product);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = RoleGroups.MasterWrite)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null)
        {
            return NotFound();
        }
        product.IsActive = false;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Deactivate", nameof(Product), id.ToString(),
            detail: $"code={product.Code}", ct: ct);
        return NoContent();
    }

    // ---- MBOM（A-40-10）----

    [HttpGet("{id:int}/bom")]
    public async Task<ActionResult<List<BomItemResponse>>> GetBom(int id, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id, ct))
        {
            return NotFound();
        }
        return await db.BomItems.AsNoTracking()
            .Where(b => b.ParentProductId == id)
            .OrderBy(b => b.ChildProduct!.Code)
            .Select(b => new BomItemResponse(
                b.Id, b.ChildProductId, b.ChildProduct!.Code, b.ChildProduct!.Name,
                b.QuantityPer, b.MakeOrBuy, b.AlternativeGroup, b.IsAlternative))
            .ToListAsync(ct);
    }

    /// <summary>MBOM明細の一括置換（設計変更 A-40-10-05 も本APIで反映）</summary>
    [HttpPut("{id:int}/bom")]
    [Authorize(Roles = RoleGroups.MasterWrite)]
    public async Task<ActionResult<List<BomItemResponse>>> ReplaceBom(
        int id, List<BomItemRequest> items, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id, ct))
        {
            return NotFound();
        }
        if (items.Any(i => i.ChildProductId == id))
        {
            return BadRequest(new ProblemDetails { Title = "品目自身をMBOMの子品目にはできません。" });
        }
        if (items.GroupBy(i => i.ChildProductId).Any(g => g.Count() > 1))
        {
            return BadRequest(new ProblemDetails { Title = "同一の子品目が重複しています。" });
        }

        var childIds = items.Select(i => i.ChildProductId).ToList();
        var validChildIds = await db.Products
            .Where(p => childIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        if (childIds.Except(validChildIds).Any())
        {
            return BadRequest(new ProblemDetails { Title = "存在しない子品目IDが含まれています。" });
        }

        var existing = await db.BomItems.Where(b => b.ParentProductId == id).ToListAsync(ct);
        db.BomItems.RemoveRange(existing);
        db.BomItems.AddRange(items.Select(i => new BomItem
        {
            ParentProductId = id,
            ChildProductId = i.ChildProductId,
            QuantityPer = i.QuantityPer,
            MakeOrBuy = i.MakeOrBuy,
            AlternativeGroup = i.AlternativeGroup,
            IsAlternative = i.IsAlternative,
        }));
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", "Bom", id.ToString(),
            detail: $"items={items.Count}", ct: ct);
        return await GetBom(id, ct);
    }

    // ---- 工順/BOP（A-40-20、I-30-20）----

    [HttpGet("{id:int}/routing")]
    public async Task<ActionResult<List<RoutingStepResponse>>> GetRouting(int id, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id, ct))
        {
            return NotFound();
        }
        return await db.Routings.AsNoTracking()
            .Where(r => r.ProductId == id)
            .OrderBy(r => r.Sequence)
            .Select(r => new RoutingStepResponse(
                r.Id, r.Sequence, r.ProcessId, r.Process!.Code, r.Process!.Name,
                r.StandardWorkMinutes, r.StandardSetupMinutes,
                r.RequiredSkillId, r.RequiredSkill != null ? r.RequiredSkill.Name : null,
                r.EquipmentId, r.ToolId, r.ControlItems, r.ChecklistId))
            .ToListAsync(ct);
    }

    /// <summary>工順（BOP）の一括置換（工程変更 A-40-20-03、I-50-30 も本APIで反映）</summary>
    [HttpPut("{id:int}/routing")]
    [Authorize(Roles = RoleGroups.MasterWrite)]
    public async Task<ActionResult<List<RoutingStepResponse>>> ReplaceRouting(
        int id, List<RoutingStepRequest> steps, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id, ct))
        {
            return NotFound();
        }
        if (steps.GroupBy(s => s.Sequence).Any(g => g.Count() > 1))
        {
            return BadRequest(new ProblemDetails { Title = "工程順序が重複しています。" });
        }

        var processIds = steps.Select(s => s.ProcessId).Distinct().ToList();
        var validProcessCount = await db.Processes.CountAsync(p => processIds.Contains(p.Id), ct);
        if (validProcessCount != processIds.Count)
        {
            return BadRequest(new ProblemDetails { Title = "存在しない工程IDが含まれています。" });
        }
        foreach (var (ids, set, label) in new[]
        {
            (steps.Where(s => s.RequiredSkillId != null).Select(s => s.RequiredSkillId!.Value), db.Skills.Select(x => x.Id), "スキル"),
            (steps.Where(s => s.EquipmentId != null).Select(s => s.EquipmentId!.Value), db.Equipments.Select(x => x.Id), "設備"),
            (steps.Where(s => s.ToolId != null).Select(s => s.ToolId!.Value), db.Tools.Select(x => x.Id), "治工具"),
            (steps.Where(s => s.ChecklistId != null).Select(s => s.ChecklistId!.Value), db.Checklists.Select(x => x.Id), "チェックリスト"),
        })
        {
            var wanted = ids.Distinct().ToList();
            if (wanted.Count > 0)
            {
                var found = await set.Where(x => wanted.Contains(x)).CountAsync(ct);
                if (found != wanted.Count)
                {
                    return BadRequest(new ProblemDetails { Title = $"存在しない{label}IDが含まれています。" });
                }
            }
        }

        var existing = await db.Routings.Where(r => r.ProductId == id).ToListAsync(ct);
        db.Routings.RemoveRange(existing);
        db.Routings.AddRange(steps.Select(s => new Routing
        {
            ProductId = id,
            Sequence = s.Sequence,
            ProcessId = s.ProcessId,
            StandardWorkMinutes = s.StandardWorkMinutes,
            StandardSetupMinutes = s.StandardSetupMinutes,
            RequiredSkillId = s.RequiredSkillId,
            EquipmentId = s.EquipmentId,
            ToolId = s.ToolId,
            ControlItems = s.ControlItems,
            ChecklistId = s.ChecklistId,
        }));
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", "Routing", id.ToString(),
            detail: $"steps={steps.Count}", ct: ct);
        return await GetRouting(id, ct);
    }

    private static ProductResponse ToResponse(Product p) =>
        new(p.Id, p.Code, p.Name, p.Unit, p.Specification, p.Type, p.StandardDefectRate, p.IsActive);
}
