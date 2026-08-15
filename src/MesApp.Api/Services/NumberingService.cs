using MesApp.Core.Abstractions;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 指図番号・ロット番号の自動採番。
/// 指図番号：MOyyyyMMdd-0001、ロット番号：品目コード-yyyyMMdd-001（B-10-10-05。日付は業務日付）
/// </summary>
public class NumberingService(MesAppDbContext db, IBusinessDateService businessDate)
{
    public async Task<string> NextOrderNoAsync(CancellationToken ct = default)
    {
        var prefix = $"MO{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.ManufacturingOrders.Where(o => o.OrderNo.StartsWith(prefix)).Select(o => o.OrderNo),
            prefix, ct);
        return $"{prefix}{seq:0000}";
    }

    public async Task<string> NextLotNumberAsync(string productCode, CancellationToken ct = default)
    {
        var prefix = $"{productCode}-{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.Lots.Where(l => l.LotNumber.StartsWith(prefix)).Select(l => l.LotNumber),
            prefix, ct);
        return $"{prefix}{seq:000}";
    }

    private static async Task<int> NextSequenceAsync(
        IQueryable<string> existingNumbers, string prefix, CancellationToken ct)
    {
        var numbers = await existingNumbers.ToListAsync(ct);
        var max = numbers
            .Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0)
            .Max();
        return max + 1;
    }
}
