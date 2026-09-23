using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// サンプル品の保管（D-40-50-01）：採取と保管の終了（払出・廃棄）。
/// 単票API（<c>SampleStoragesController</c>）と実績CSV取込の両方から呼ぶ。サンプルの状態はここでだけ変更する。
/// 保存と監査ログまで行う。トランザクションは呼び出し側が張る。
/// </summary>
public sealed class SampleStorageService(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger)
{
    /// <summary>
    /// サンプルの採取。採取した分は在庫から抜く
    /// （保管棚へ移り、出荷・投入には使えない。在庫に残すと引当・先入れ先出しの対象になってしまう）。
    /// sampleNo を渡すとその番号で登録する（CSV取込用。空なら自動採番）
    /// </summary>
    public async Task<Outcome<SampleStorage>> CollectAsync(
        SampleCollectRequest request, string? sampleNo, string? userId, CancellationToken ct)
    {
        var lot = await db.Lots.FirstOrDefaultAsync(l => l.Id == request.LotId, ct);
        if (lot is null)
        {
            return Outcome<SampleStorage>.NotFound(ApiText.T("ロットID {0} は登録されていません。", request.LotId));
        }
        if (!await db.Locations.AnyAsync(l => l.Id == request.StorageLocationId && l.IsActive, ct))
        {
            return Outcome<SampleStorage>.Invalid(ApiText.T("保管場所が見つからないか無効です。"));
        }
        if (request.InspectionOrderId is { } inspectionId
            && !await db.InspectionOrders.AnyAsync(i => i.Id == inspectionId, ct))
        {
            return Outcome<SampleStorage>.Invalid(ApiText.T("検査指示ID {0} は登録されていません。", inspectionId));
        }

        // 採取元の在庫は、サンプルの保管場所ではなく現物があった場所から抜く
        var from = await db.InventoryStocks.AsNoTracking()
            .Where(s => s.LotId == lot.Id && s.Quantity > 0)
            .OrderByDescending(s => s.Quantity)
            .Select(s => (int?)s.LocationId)
            .FirstOrDefaultAsync(ct);
        if (from is null)
        {
            return Outcome<SampleStorage>.Invalid(ApiText.T("ロット {0} の在庫がありません。", lot.LotNumber));
        }

        if (string.IsNullOrWhiteSpace(sampleNo))
        {
            sampleNo = await numbering.NextSampleNoAsync(ct);
        }
        else if (NumberingService.IsAutoNumberFormat(sampleNo, NumberingService.SampleNoPrefix))
        {
            return Outcome<SampleStorage>.Invalid(
                ApiText.T("サンプル番号 '{0}' は自動採番の形式（{1}〜）と重なるため指定できません。", sampleNo, NumberingService.SampleNoPrefix));
        }
        else if (await db.SampleStorages.AnyAsync(x => x.SampleNo == sampleNo, ct))
        {
            return Outcome<SampleStorage>.Conflict(ApiText.T("サンプル番号 '{0}' は既に存在します。", sampleNo));
        }

        var sample = new SampleStorage
        {
            SampleNo = sampleNo,
            ProductId = lot.ProductId,
            LotId = lot.Id,
            InspectionOrderId = request.InspectionOrderId,
            Quantity = request.Quantity,
            StorageLocationId = request.StorageLocationId,
            CollectedOn = businessDate.Today,
            RetainUntil = request.RetainUntil,
            Status = SampleStorageStatus.Stored,
            CollectedByUserId = userId,
            Note = request.Note,
        };
        db.SampleStorages.Add(sample);

        try
        {
            await inventory.RemoveAsync(lot, from.Value, request.Quantity,
                InventoryTransactionType.SampleRetention, userId,
                note: $"サンプル採取 {sample.SampleNo}", ct: ct);
        }
        catch (InventoryException ex)
        {
            return Outcome<SampleStorage>.Invalid(ex.Message);
        }

        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "SampleCollect", nameof(SampleStorage),
            sample.Id.ToString(),
            detail: $"sample={sample.SampleNo} lot={lot.LotNumber} qty={request.Quantity}", ct: ct);
        return Outcome<SampleStorage>.Ok(sample);
    }

    /// <summary>
    /// 保管の終了（払出・廃棄）。**在庫は動かさない**（採取時に既に抜いてあるため）。
    /// 保管期限前の処分も禁止しない——再試験のための払出は期限前に起こるのが普通で、
    /// 止めると運用が画面の外へ出てしまう。期限前だったかどうかは監査ログに残す
    /// </summary>
    public async Task<Outcome<SampleStorage>> CloseAsync(
        int id, SampleCloseRequest request, string? userId, CancellationToken ct)
    {
        var sample = await db.SampleStorages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (sample is null)
        {
            return Outcome<SampleStorage>.NotFound(ApiText.T("サンプルが見つかりません。"));
        }
        if (sample.Status != SampleStorageStatus.Stored)
        {
            return Outcome<SampleStorage>.Conflict(ApiText.T("サンプル {0} は既に保管を終えています。", sample.SampleNo));
        }
        if (request.Status is not (SampleStorageStatus.Consumed or SampleStorageStatus.Disposed))
        {
            return Outcome<SampleStorage>.Invalid(ApiText.T("払出済か廃棄済かを指定してください。"));
        }

        var before = sample.Status;
        var beforeRetentionEnd = sample.RetainUntil is { } until && businessDate.Today <= until;
        sample.Status = request.Status;
        sample.ClosedOn = businessDate.Today;
        sample.ClosedByUserId = userId;
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
        return Outcome<SampleStorage>.Ok(sample);
    }
}
