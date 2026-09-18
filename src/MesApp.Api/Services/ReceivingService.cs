using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>受入登録の結果（Errorがあれば未登録。IsConflictはロット番号の重複）</summary>
public sealed record ReceivingOutcome(Lot? Lot, Product? Product, string? Error, bool IsConflict = false);

/// <summary>
/// 受入登録（D-10-10-02。ロット生成＋在庫計上）。単票API（<c>ReceivingController</c>）と
/// 実績CSV取込（<c>ActualCsvService</c>）の両方から呼ぶ。
/// <para>
/// <b>トランザクションは呼び出し側が張る。</b>Lot.Idの確定と在庫計上で保存が2回に分かれるため
/// 途中で失敗すると「在庫のないロット」が残るが、CSV取込は全行を1つのトランザクションで
/// まとめる必要があり、ここで張ると入れ子になる。
/// </para>
/// </summary>
public sealed class ReceivingService(
    MesAppDbContext db,
    InventoryService inventory,
    NumberingService numbering,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger)
{
    public async Task<ReceivingOutcome> ReceiveAsync(ReceivingRequest request, string? userId, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId, ct);
        if (product is null || !product.IsActive)
        {
            return new ReceivingOutcome(null, null, "存在しない（または無効な）品目IDです。");
        }
        if (!await db.Locations.AnyAsync(l => l.Id == request.LocationId && l.IsActive, ct))
        {
            return new ReceivingOutcome(null, product, "存在しない（または無効な）ロケーションIDです。");
        }

        var lotNumber = request.LotNumber;
        if (string.IsNullOrWhiteSpace(lotNumber))
        {
            lotNumber = await numbering.NextLotNumberAsync(product.Code, ct);
        }
        else if (await db.Lots.AnyAsync(l => l.LotNumber == lotNumber, ct))
        {
            return new ReceivingOutcome(null, product, $"ロット番号 '{lotNumber}' は既に存在します。", IsConflict: true);
        }

        var lot = new Lot
        {
            LotNumber = lotNumber,
            ProductId = product.Id,
            InitialQuantity = request.Quantity,
            OriginType = LotOriginType.Receiving,
            ManufacturedOn = businessDate.Today,
            ExpiresOn = request.ExpiresOn,
            StockStatus = LotStockStatus.Normal,
        };
        db.Lots.Add(lot);
        await db.SaveChangesAsync(ct); // Lot.Idの確定

        await inventory.AddAsync(lot, request.LocationId, request.Quantity,
            InventoryTransactionType.Receipt, userId, note: request.Note, ct: ct);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Inventory", "Receive", nameof(Lot), lot.Id.ToString(),
            detail: $"lot={lotNumber}, product={product.Code}, qty={request.Quantity}", ct: ct);
        return new ReceivingOutcome(lot, product, null);
    }
}
