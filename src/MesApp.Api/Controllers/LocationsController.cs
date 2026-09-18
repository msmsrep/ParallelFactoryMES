using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>ロケーションマスタ（Spec.md 5.1 Location。D-50-20-01）</summary>
[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationsController(MesAppDbContext db, IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LocationResponse>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var query = db.Locations.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(l => l.IsActive);
        }
        return await query.OrderBy(l => l.Code).Include(l => l.WorkCenter)
            .Select(l => ToResponse(l))
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LocationResponse>> Get(int id, CancellationToken ct)
    {
        var l = await db.Locations.AsNoTracking().Include(x => x.WorkCenter)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return l is null ? NotFound() : ToResponse(l);
    }

    [HttpPost]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<LocationResponse>> Create(LocationRequest request, CancellationToken ct)
    {
        if (await db.Locations.AnyAsync(l => l.Code == request.Code, ct))
        {
            return this.ConflictProblem($"ロケーションコード '{request.Code}' は既に存在します。");
        }
        var workCenter = await FindWorkCenterAsync(request.WorkCenterId, ct);
        if (request.WorkCenterId is { } missing && workCenter is null)
        {
            return this.BadRequestProblem($"作業区（ID {missing}）が見つかりません。");
        }
        if (WorkCenterHierarchyPolicy.CheckLocationPlacement(workCenter) is { } reason)
        {
            return this.BadRequestProblem(reason);
        }
        var l = new Location
        {
            Code = request.Code,
            AreaType = request.AreaType,
            ShelfNo = request.ShelfNo,
            WorkCenterId = request.WorkCenterId,
        };
        db.Locations.Add(l);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Create", nameof(Location), l.Id.ToString(),
            detail: $"code={l.Code}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = l.Id }, ToResponse(l, workCenter));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public async Task<ActionResult<LocationResponse>> Update(int id, LocationRequest request, CancellationToken ct)
    {
        var l = await db.Locations.FindAsync([id], ct);
        if (l is null)
        {
            return NotFound();
        }
        if (await db.Locations.AnyAsync(x => x.Code == request.Code && x.Id != id, ct))
        {
            return this.ConflictProblem($"ロケーションコード '{request.Code}' は既に存在します。");
        }
        var workCenter = await FindWorkCenterAsync(request.WorkCenterId, ct);
        if (request.WorkCenterId is { } missing && workCenter is null)
        {
            return this.BadRequestProblem($"作業区（ID {missing}）が見つかりません。");
        }
        if (WorkCenterHierarchyPolicy.CheckLocationPlacement(workCenter) is { } reason)
        {
            return this.BadRequestProblem(reason);
        }
        l.Code = request.Code;
        l.AreaType = request.AreaType;
        l.ShelfNo = request.ShelfNo;
        l.WorkCenterId = request.WorkCenterId;
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Master", "Update", nameof(Location), id.ToString(),
            detail: $"code={l.Code}", ct: ct);
        return ToResponse(l, workCenter);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = MesRoleGroups.MasterWrite)]
    public Task<IActionResult> Deactivate(int id, CancellationToken ct) =>
        this.DeactivateMasterAsync<Location>(db, auditLogger, id, l => $"code={l.Code}", ct);

    private async Task<WorkCenter?> FindWorkCenterAsync(int? id, CancellationToken ct) =>
        id is { } value
            ? await db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(w => w.Id == value, ct)
            : null;

    /// <summary>
    /// 推奨ロケーション（D-10-30-03、D-40-40-03）。入庫先の候補を優先度順に返す。
    /// <para>
    /// 推奨の根拠は3段階で、上から順に強い。
    /// ①品目マスタの既定ロケーション（固定ロケーション運用）
    /// ②同じ品目の在庫が既にあるロケーション（数量の多い順。散らばると探せなくなる）
    /// ③品目区分に対応するエリアのロケーション（製品→製品倉庫、それ以外→部材倉庫）。
    /// **強制はしない**——実地では棚が埋まっていることがあり、機械が決められるのは「どこが妥当か」まで。
    /// </para>
    /// </summary>
    [HttpGet("recommendations")]
    public async Task<ActionResult<List<LocationRecommendationResponse>>> Recommendations(
        [FromQuery] int productId, [FromQuery] int limit = 5, CancellationToken ct = default)
    {
        var product = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId, ct);
        if (product is null)
        {
            return this.NotFoundProblem($"品目ID {productId} は登録されていません。");
        }

        // その品目が今どこにどれだけあるか（②の並び順と、全候補に添える現在庫）
        var stocks = await db.InventoryStocks.AsNoTracking()
            .Where(s => s.ProductId == productId && s.Quantity > 0)
            .GroupBy(s => s.LocationId)
            .Select(g => new { LocationId = g.Key, Quantity = g.Sum(s => s.Quantity) })
            .ToListAsync(ct);
        var quantityByLocation = stocks.ToDictionary(x => x.LocationId, x => x.Quantity);

        var defaultArea = product.Type == ProductType.Product
            ? LocationAreaType.ProductWarehouse
            : LocationAreaType.MaterialWarehouse;
        var locations = await db.Locations.AsNoTracking()
            .Where(l => l.IsActive)
            .OrderBy(l => l.Code)
            .ToListAsync(ct);
        var byId = locations.ToDictionary(l => l.Id);

        // 同じロケーションが複数の理由に当たることがある。最初（＝最も強い理由）だけを残す
        var result = new List<LocationRecommendationResponse>();
        void Add(Location location, string reason)
        {
            if (result.Any(r => r.LocationId == location.Id))
            {
                return;
            }
            result.Add(new LocationRecommendationResponse(
                location.Id, location.Code, location.AreaType, location.ShelfNo, reason,
                quantityByLocation.GetValueOrDefault(location.Id)));
        }

        if (product.DefaultLocationId is { } defaultId && byId.TryGetValue(defaultId, out var defaultLocation))
        {
            Add(defaultLocation, "品目マスタの既定ロケーション");
        }
        foreach (var stock in stocks.OrderByDescending(s => s.Quantity))
        {
            if (byId.TryGetValue(stock.LocationId, out var location))
            {
                Add(location, $"同じ品目の在庫がある（{stock.Quantity:0.##} {product.Unit}）");
            }
        }
        foreach (var location in locations.Where(l => l.AreaType == defaultArea))
        {
            Add(location, $"品目区分「{ProductTypeLabel(product.Type)}」の既定エリア");
        }

        return result.Take(Math.Clamp(limit, 1, 20)).ToList();
    }

    /// <summary>推奨理由に出す品目区分の日本語（画面と同じ語を使う）</summary>
    private static string ProductTypeLabel(ProductType type) => type switch
    {
        ProductType.Product => "製品",
        ProductType.SemiFinished => "半製品・中間品",
        _ => "部材",
    };

    /// <summary>作業区は未設定でもよいため、コード・名称はnull許容のまま返す</summary>
    private static LocationResponse ToResponse(Location l, WorkCenter? workCenter = null)
    {
        var wc = workCenter ?? l.WorkCenter;
        return new(l.Id, l.Code, l.AreaType, l.ShelfNo, l.IsActive, l.WorkCenterId, wc?.Code, wc?.Name);
    }
}
