using MesApp.Api.Localization;
using System.Linq.Expressions;
using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// サンプル品の保管管理（Spec.md 3.4 その他。D-40-50-01）。
/// 検査で採取したサンプルは保管期限まで捨てられないため、所在と期限を追う。
/// サンプル取得・識別ラベル（C-20-40-03）は検査側の話で、こちらは**取得後の保管**を扱う。
/// </summary>
[ApiController]
[Route("api/sample-storages")]
[Authorize]
public class SampleStoragesController(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>サンプルの一覧（状態・品目・保管期限で絞り込む）</summary>
    [HttpGet]
    public async Task<ActionResult<List<SampleStorageResponse>>> List(
        [FromQuery] SampleStorageStatus? status = null,
        [FromQuery] int? productId = null,
        [FromQuery] bool retentionOverOnly = false,
        CancellationToken ct = default)
    {
        var query = db.SampleStorages.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            query = query.Where(x => x.Status == status);
        }
        if (productId is not null)
        {
            query = query.Where(x => x.ProductId == productId);
        }
        if (retentionOverOnly)
        {
            // 「もう処分してよいもの」を出す。保管中のものだけが対象
            var today = businessDate.Today;
            query = query.Where(x => x.Status == SampleStorageStatus.Stored
                                     && x.RetainUntil != null && x.RetainUntil < today);
        }
        var rows = await query.OrderByDescending(x => x.Id).Select(Projection).ToListAsync(ct);
        return rows.Select(Fill).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SampleStorageResponse>> Get(int id, CancellationToken ct)
    {
        var row = await db.SampleStorages.AsNoTracking()
            .Where(x => x.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        return row is null ? NotFound() : Fill(row);
    }

    /// <summary>
    /// サンプルの採取（D-40-50-01）。採取した分は在庫から抜く
    /// （保管棚へ移り、出荷・投入には使えない。在庫に残すと引当・先入れ先出しの対象になってしまう）
    /// </summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<SampleStorageResponse>> Collect(
        SampleCollectRequest request, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return this.NotFoundProblem(ApiText.T("ロットID {0} は登録されていません。", request.LotId));
        }
        if (!await db.Locations.AnyAsync(l => l.Id == request.StorageLocationId && l.IsActive, ct))
        {
            return this.BadRequestProblem(ApiText.T("保管場所が見つからないか無効です。"));
        }
        if (request.InspectionOrderId is { } inspectionId
            && !await db.InspectionOrders.AnyAsync(i => i.Id == inspectionId, ct))
        {
            return this.BadRequestProblem(ApiText.T("検査指示ID {0} は登録されていません。", inspectionId));
        }

        // 採取元の在庫は、サンプルの保管場所ではなく現物があった場所から抜く
        var from = await db.InventoryStocks.AsNoTracking()
            .Where(s => s.LotId == lot.Id && s.Quantity > 0)
            .OrderByDescending(s => s.Quantity)
            .Select(s => (int?)s.LocationId)
            .FirstOrDefaultAsync(ct);
        if (from is null)
        {
            return this.BadRequestProblem(
                ApiText.T("ロット {0} の在庫がありません。", lot.LotNumber));
        }

        var sample = new SampleStorage
        {
            SampleNo = await numbering.NextSampleNoAsync(ct),
            ProductId = lot.ProductId,
            LotId = lot.Id,
            InspectionOrderId = request.InspectionOrderId,
            Quantity = request.Quantity,
            StorageLocationId = request.StorageLocationId,
            CollectedOn = businessDate.Today,
            RetainUntil = request.RetainUntil,
            Status = SampleStorageStatus.Stored,
            CollectedByUserId = CurrentUserId,
            Note = request.Note,
        };
        db.SampleStorages.Add(sample);

        try
        {
            await inventory.RemoveAsync(lot, from.Value, request.Quantity,
                InventoryTransactionType.SampleRetention, CurrentUserId,
                note: $"サンプル採取 {sample.SampleNo}", ct: ct);
        }
        catch (InventoryException ex)
        {
            return this.BadRequestProblem(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "SampleCollect", nameof(SampleStorage),
            sample.Id.ToString(),
            detail: $"sample={sample.SampleNo} lot={lot.LotNumber} qty={request.Quantity}", ct: ct);
        return CreatedAtAction(nameof(Get), new { id = sample.Id }, await LoadAsync(sample.Id, ct));
    }

    /// <summary>
    /// 保管の終了（払出・廃棄）。**在庫は動かさない**（採取時に既に抜いてあるため）。
    /// 保管期限前の処分も禁止しない——再試験のための払出は期限前に起こるのが普通で、
    /// 止めると運用が画面の外へ出てしまう。期限前だったかどうかは監査ログに残す
    /// </summary>
    [HttpPost("{id:int}/close")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<SampleStorageResponse>> Close(
        int id, SampleCloseRequest request, CancellationToken ct)
    {
        var sample = await db.SampleStorages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (sample is null)
        {
            return NotFound();
        }
        if (sample.Status != SampleStorageStatus.Stored)
        {
            return this.ConflictProblem(
                ApiText.T("サンプル {0} は既に保管を終えています。", sample.SampleNo));
        }
        if (request.Status is not (SampleStorageStatus.Consumed or SampleStorageStatus.Disposed))
        {
            return this.BadRequestProblem(ApiText.T("払出済か廃棄済かを指定してください。"));
        }

        var before = sample.Status;
        var beforeRetentionEnd = sample.RetainUntil is { } until && businessDate.Today <= until;
        sample.Status = request.Status;
        sample.ClosedOn = businessDate.Today;
        sample.ClosedByUserId = CurrentUserId;
        sample.Note = request.Note ?? sample.Note;
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Inventory", "SampleClose", nameof(SampleStorage), id.ToString(),
            detail: new
            {
                before,
                after = sample.Status,
                reason = request.Note,
                beforeRetentionEnd,
            }, ct: ct);
        return await LoadAsync(id, ct);
    }

    private async Task<SampleStorageResponse> LoadAsync(int id, CancellationToken ct) =>
        Fill(await db.SampleStorages.AsNoTracking()
            .Where(x => x.Id == id).Select(Projection).FirstAsync(ct));

    /// <summary>
    /// 期限の判定は業務日付基準で、SQLへ持ち込まず取得後に埋める
    /// （検査機の校正期限 C-20-50-03 と同じ形）
    /// </summary>
    private SampleStorageResponse Fill(SampleStorageResponse row)
    {
        if (row.RetainUntil is not { } until)
        {
            return row;
        }
        var days = until.DayNumber - businessDate.Today.DayNumber;
        return row with
        {
            IsRetentionOver = row.Status == SampleStorageStatus.Stored && days < 0,
            DaysUntilRetentionEnd = days,
        };
    }

    /// <summary>一覧・単票で共通の射影。EF Core が翻訳できるよう**式として持つ**</summary>
    private static readonly Expression<Func<SampleStorage, SampleStorageResponse>> Projection =
        x => new SampleStorageResponse(
            x.Id, x.SampleNo,
            x.ProductId, x.Product!.Code, x.Product!.Name, x.Product!.Unit,
            x.LotId, x.Lot!.LotNumber,
            x.InspectionOrderId, x.InspectionOrder == null ? null : x.InspectionOrder.OrderNo,
            x.Quantity,
            x.StorageLocationId, x.StorageLocation!.Code,
            x.CollectedOn, x.RetainUntil, x.Status,
            x.ClosedOn, x.Note,
            false, null);
}
