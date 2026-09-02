using System.Data.Common;
using MesApp.Core.Abstractions;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 指図番号・ロット番号の自動採番。
/// 指図番号：MOyyyyMMdd-0001、ロット番号：品目コード-yyyyMMdd-001（B-10-10-05。日付は業務日付）
/// </summary>
/// <remarks>
/// <para>
/// 払い出しは <see cref="NumberSequence"/> の1行を更新してから読む形にして不可分にする。
/// 既存番号の最大値+1では、同時に採番したリクエストが同じ番号を得て一意制約違反になる。
/// </para>
/// <para>
/// <b>変更追跡を使わない</b>（<c>ExecuteUpdate</c> と生SQL）。採番は呼び出し側が他のエンティティを
/// 変更したあとに呼ばれることがあり（検査の不合格judge・不適合の対応指示など）、
/// ここで <c>SaveChanges</c> すると呼び出し側の未確定の変更まで書き込んでしまう。
/// </para>
/// </remarks>
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

    public async Task<string> NextPickingNoAsync(CancellationToken ct = default)
    {
        var prefix = $"PK{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.PickingOrders.Where(p => p.OrderNo.StartsWith(prefix)).Select(p => p.OrderNo),
            prefix, ct);
        return $"{prefix}{seq:0000}";
    }

    public async Task<string> NextShippingNoAsync(CancellationToken ct = default)
    {
        var prefix = $"SH{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.ShippingOrders.Where(s => s.ShippingNo.StartsWith(prefix)).Select(s => s.ShippingNo),
            prefix, ct);
        return $"{prefix}{seq:0000}";
    }

    public async Task<string> NextStocktakeNoAsync(CancellationToken ct = default)
    {
        var prefix = $"ST{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.Stocktakes.Where(s => s.StocktakeNo.StartsWith(prefix)).Select(s => s.StocktakeNo),
            prefix, ct);
        return $"{prefix}{seq:0000}";
    }

    public async Task<string> NextInspectionNoAsync(CancellationToken ct = default)
    {
        var prefix = $"IN{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.InspectionOrders.Where(i => i.OrderNo.StartsWith(prefix)).Select(i => i.OrderNo),
            prefix, ct);
        return $"{prefix}{seq:0000}";
    }

    public async Task<string> NextNonconformanceNoAsync(CancellationToken ct = default)
    {
        var prefix = $"NC{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.NonconformanceReports.Where(n => n.ReportNo.StartsWith(prefix)).Select(n => n.ReportNo),
            prefix, ct);
        return $"{prefix}{seq:0000}";
    }

    public async Task<string> NextMaintenanceNoAsync(CancellationToken ct = default)
    {
        var prefix = $"MT{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.MaintenanceOrders.Where(m => m.OrderNo.StartsWith(prefix)).Select(m => m.OrderNo),
            prefix, ct);
        return $"{prefix}{seq:0000}";
    }

    public async Task<string> NextJudgmentNoAsync(CancellationToken ct = default)
    {
        var prefix = $"SJ{businessDate.Today:yyyyMMdd}-";
        var seq = await NextSequenceAsync(
            db.ShipmentJudgments.Where(j => j.JudgmentNo.StartsWith(prefix)).Select(j => j.JudgmentNo),
            prefix, ct);
        return $"{prefix}{seq:0000}";
    }

    /// <summary>
    /// プレフィックスの連番を1つ払い出す。
    /// 行が無ければ既存番号の最大値から続きになるよう作る（採番テーブル導入前のデータとの連続性）。
    /// </summary>
    private async Task<int> NextSequenceAsync(
        IQueryable<string> existingNumbers, string prefix, CancellationToken ct)
    {
        // 呼び出し側が既にトランザクションを開いていればそれに乗る。
        // 開いていなければ、更新と読み取りが割り込まれないよう自前で開く
        var ownsTransaction = db.Database.CurrentTransaction is null;
        var transaction = ownsTransaction ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            var value = await IncrementAsync(prefix, ct)
                        ?? await CreateSequenceAsync(existingNumbers, prefix, ct);
            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }
            return value;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    /// <summary>連番を1つ進めて新しい値を返す（行が無ければnull）</summary>
    private async Task<int?> IncrementAsync(string prefix, CancellationToken ct)
    {
        var updated = await db.NumberSequences
            .Where(s => s.Prefix == prefix)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastValue, x => x.LastValue + 1), ct);
        if (updated == 0)
        {
            return null;
        }
        // 更新で書き込みロックを取っているため、この読み取りに他の採番は割り込めない
        return await db.NumberSequences.AsNoTracking()
            .Where(s => s.Prefix == prefix)
            .Select(s => s.LastValue)
            .FirstAsync(ct);
    }

    /// <summary>初回の払い出し。既存番号の続きから始める</summary>
    private async Task<int> CreateSequenceAsync(
        IQueryable<string> existingNumbers, string prefix, CancellationToken ct)
    {
        var numbers = await existingNumbers.ToListAsync(ct);
        var value = numbers
            .Select(n => int.TryParse(n[prefix.Length..], out var v) ? v : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        try
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO NumberSequences (Prefix, LastValue) VALUES ({prefix}, {value})", ct);
            return value;
        }
        catch (DbException)
        {
            // 同時に別のリクエストが作った。作られた行を進めて払い出す
            return await IncrementAsync(prefix, ct)
                   ?? throw new InvalidOperationException($"採番列 '{prefix}' を作成できませんでした。");
        }
    }
}
