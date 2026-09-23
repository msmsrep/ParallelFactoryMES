using MesApp.Api.Localization;
using System.Linq.Expressions;
using MesApp.Api.Policies;
using MesApp.Api.Services;
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
public class ProductsController(
    MesAppDbContext db, IAuditLogger auditLogger, ProductStructureService structure) : ControllerBase
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
        return await query.OrderBy(p => p.Code).Select(Projection).ToListAsync(ct);
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
        return await query.OrderBy(p => p.Code).Select(Projection)
            .ToOptionsResultAsync(options, ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductResponse>> Get(int id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        return product is null ? NotFound() : product;
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ProductResponse>> Create(ProductRequest request, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Code == request.Code, ct))
        {
            return this.ConflictProblem(ApiText.T("品目コード '{0}' は既に存在します。", request.Code));
        }
        if (await CheckDefaultLocationAsync(request.DefaultLocationId, ct) is { } invalid)
        {
            return this.BadRequestProblem(invalid);
        }

        var product = new Product
        {
            Code = request.Code,
            Name = request.Name,
            Unit = request.Unit,
            Specification = request.Specification,
            Type = request.Type,
            StandardDefectRate = request.StandardDefectRate,
            DefaultLocationId = request.DefaultLocationId,
        };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Product), product.Id.ToString(),
            detail: $"code={product.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, await LoadAsync(product.Id, ct));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<ProductResponse>> Update(int id, ProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null)
        {
            return NotFound();
        }
        if (await db.Products.AnyAsync(p => p.Code == request.Code && p.Id != id, ct))
        {
            return this.ConflictProblem(ApiText.T("品目コード '{0}' は既に存在します。", request.Code));
        }
        if (await CheckDefaultLocationAsync(request.DefaultLocationId, ct) is { } invalid)
        {
            return this.BadRequestProblem(invalid);
        }

        product.Code = request.Code;
        product.Name = request.Name;
        product.Unit = request.Unit;
        product.Specification = request.Specification;
        product.Type = request.Type;
        product.StandardDefectRate = request.StandardDefectRate;
        product.DefaultLocationId = request.DefaultLocationId;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Product), id.ToString(),
            detail: $"code={product.Code}", ct: ct);
        return await LoadAsync(id, ct);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public Task<IActionResult> Deactivate(int id, CancellationToken ct) =>
        this.DeactivateMasterAsync<Product>(db, auditLogger, id, p => $"code={p.Code}", ct,
            onDeactivating: p => p.UpdatedAt = DateTimeOffset.UtcNow);

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
                b.QuantityPer, b.MakeOrBuy, b.AlternativeGroup, b.IsAlternative, b.RoutingSequence))
            .ToListAsync(ct);
    }

    /// <summary>MBOM明細の一括置換（設計変更 A-40-10-05 も本APIで反映）</summary>
    [HttpPut("{id:int}/bom")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<List<BomItemResponse>>> ReplaceBom(
        int id, List<BomItemRequest> items, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id, ct))
        {
            return NotFound();
        }
        if (await structure.ReplaceBomAsync(id, items, ct) is { } error)
        {
            return this.BadRequestProblem(error);
        }
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
            .Include(r => r.EquipmentCandidates).ThenInclude(c => c.Equipment)
            .Include(r => r.WorkProcedure)
            .Include(r => r.ControlItemLinks).ThenInclude(l => l.ControlItem)
            .Where(r => r.ProductId == id)
            .OrderBy(r => r.Sequence)
            .Select(r => new RoutingStepResponse(
                r.Id, r.Sequence, r.ProcessId, r.Process!.Code, r.Process!.Name,
                r.StandardWorkMinutes, r.StandardSetupMinutes,
                r.RequiredSkillId, r.RequiredSkill != null ? r.RequiredSkill.Name : null,
                r.EquipmentId, r.ToolId, r.ControlItems, r.ChecklistId,
                r.WorkCenterId, r.WorkCenter != null ? r.WorkCenter.Code : null,
                r.WorkCenter != null ? r.WorkCenter.Name : null,
                r.EquipmentCandidates.OrderBy(c => c.Equipment!.AssetNo)
                    .Select(c => c.Equipment!.AssetNo).ToList(),
                r.EquipmentCandidates.OrderBy(c => c.Equipment!.AssetNo)
                    .Select(c => c.EquipmentId).ToList(),
                r.WorkProcedureId,
                r.WorkProcedure != null ? r.WorkProcedure.ProcedureNo : null,
                r.WorkProcedure != null ? r.WorkProcedure.Title : null,
                r.ControlItemLinks.OrderBy(l => l.ControlItem!.Code)
                    .Select(l => l.ControlItemId).ToList(),
                r.ControlItemLinks.OrderBy(l => l.ControlItem!.Code)
                    .Select(l => l.ControlItem!.Code).ToList()))
            .ToListAsync(ct);
    }

    /// <summary>工順（BOP）の一括置換（工程変更 A-40-20-03、I-50-30 も本APIで反映）</summary>
    [HttpPut("{id:int}/routing")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<List<RoutingStepResponse>>> ReplaceRouting(
        int id, List<RoutingStepRequest> steps, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id, ct))
        {
            return NotFound();
        }
        if (await structure.ReplaceRoutingAsync(id, steps, ct) is { } error)
        {
            return this.BadRequestProblem(error);
        }
        return await GetRouting(id, ct);
    }


    // ---- 設計変更の影響確認（J-40-40-01/03）----

    /// <summary>設計変更（MBOM・工順の改訂）の影響範囲。展開済みの指図に改訂が届かないことを改訂者に見せる</summary>
    [HttpGet("{id:int}/change-impact")]
    public async Task<ActionResult<DesignChangeImpactResponse>> GetChangeImpact(int id, CancellationToken ct) =>
        await structure.GetChangeImpactAsync(id, ct) is { } impact ? impact : NotFound();

    /// <summary>既定ロケーションの実在チェック（条件は CSV 取込と共通。<see cref="ProductStructurePolicy"/>）</summary>
    private async Task<string?> CheckDefaultLocationAsync(int? locationId, CancellationToken ct)
    {
        if (locationId is not { } id)
        {
            return null;
        }
        return await ProductStructurePolicy.AssignableDefaultLocations(db.Locations).AnyAsync(l => l.Id == id, ct)
            ? null
            : ApiText.T("既定ロケーション（ID {0}）が見つからないか無効です。", id);
    }

    private async Task<ProductResponse> LoadAsync(int id, CancellationToken ct) =>
        await db.Products.AsNoTracking().Where(p => p.Id == id).Select(Projection).FirstAsync(ct);

    /// <summary>
    /// 一覧・単票で共通の射影。既定ロケーションのコードを添えるため**式として持つ**
    /// （EF Core はメソッド呼び出しをSQLへ翻訳できない）
    /// </summary>
    private static readonly Expression<Func<Product, ProductResponse>> Projection =
        p => new ProductResponse(
            p.Id, p.Code, p.Name, p.Unit, p.Specification, p.Type, p.StandardDefectRate, p.IsActive,
            p.DefaultLocationId, p.DefaultLocation == null ? null : p.DefaultLocation.Code);
}
