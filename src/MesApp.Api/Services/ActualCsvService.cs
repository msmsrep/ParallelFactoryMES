using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 実績（取引データ）のCSV一括取込（Spec.md 3.8）。
/// <para>
/// 行ごとに<b>単票APIと同じ業務サービスを呼ぶ</b>（受入なら <see cref="ReceivingService"/>）。
/// 実績は在庫・ロット・状態履歴を連鎖して動かすため、マスタCSVのように行をエンティティへ
/// 直接写すと、判定や監査ログの付け忘れがそのまま迂回経路になる。
/// </para>
/// <para>
/// 全行を1つのトランザクションで処理し、<b>1行でもエラーがあれば全件ロールバック</b>する。
/// 行は保存しながら進めるため、後の行は前の行の結果（採番済みロット等）を前提にできる。
/// 検証のみ（dryRun）も実際に登録してからロールバックする——ロット番号の重複のように
/// 前の行を登録しないと判定できない条件があるため。
/// </para>
/// </summary>
public sealed class ActualCsvService(
    MesAppDbContext db,
    ReceivingService receiving,
    IAuditLogger auditLogger)
{
    public async Task<CsvImportResult> ImportAsync(
        ActualCsvKind kind, string csvText, bool dryRun, string? userId, CancellationToken ct)
    {
        var errors = new List<CsvImportError>();
        var table = CsvImport.Prepare(kind.Info, csvText, errors);
        if (table is null)
        {
            return CsvImport.Result(kind.Info, 0, 0, 0, dryRun, errors);
        }

        var created = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            created = kind.Info.Kind switch
            {
                ActualCsvKinds.Receiving => await ImportReceivingAsync(table, errors, userId, ct),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };

            if (errors.Count == 0 && !dryRun)
            {
                await auditLogger.LogAsync("Actual", "CsvImport", kind.Info.Kind, null,
                    detail: $"rows={table.Rows.Count}, created={created}", ct: ct);
                await transaction.CommitAsync(ct);
            }
            else
            {
                await transaction.RollbackAsync(ct);
            }
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(ct);
            errors.Add(new CsvImportError(0, $"DBへの反映に失敗しました：{ex.InnerException?.Message ?? ex.Message}"));
        }

        return CsvImport.Result(kind.Info, table.Rows.Count, created, 0, dryRun, errors);
    }

    /// <summary>受入（D-10-10-02）。1行＝1ロットの受入登録</summary>
    private async Task<int> ImportReceivingAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        // 無効なマスタは候補に入れない（単票APIも無効な品目・ロケーションへの受入を拒否する）
        var productIds = await db.Products.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);
        var locationIds = await db.Locations.AsNoTracking().Where(l => l.IsActive)
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("ProductCode");
            var productId = reader.Reference("ProductCode", null, productIds, "有効な品目");
            var quantity = reader.NumberOrNull("Quantity", null, 0.000001m);
            reader.RequiredText("LocationCode");
            var locationId = reader.Reference("LocationCode", null, locationIds, "有効なロケーション");
            var lotNumber = reader.Text("LotNumber", null, 60);
            var expiresOn = reader.DateOrNull("ExpiresOn", null);
            var note = reader.Text("Note", null, 500);
            if (quantity is null && !reader.Failed)
            {
                reader.Fail("Quantity（受入数量）は必須です。");
            }
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await receiving.ReceiveAsync(
                new ReceivingRequest(productId!.Value, quantity!.Value, locationId!.Value, lotNumber, expiresOn, note),
                userId, ct);
            if (outcome.Error is not null)
            {
                reader.Fail(outcome.Error);
                continue;
            }
            created++;
        }
        return created;
    }
}
