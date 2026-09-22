using System.Linq.Expressions;
using MesApp.Api.Localization;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Policies;

/// <summary>
/// 生産計画の登録判定（Spec.md 5.2 ProductionPlan。A-30-10-01）。
/// <para>
/// 計画は単票API（<c>ProductionPlansController</c>）とマスタCSV（<c>MasterCsvService</c>）の2つの経路から入るため、
/// 参照先の存在・計画数量・キーの同一性の判定はここに集める。片方だけ直すと、画面では登録できない計画が
/// CSVからは入る（またはその逆）ことになる。
/// </para>
/// <para>
/// キーは 製造日・品目・工程・作業区。作業区なし（NULL）もキーの1値として扱い、作業区なしどうしは同じキーとみなす。
/// DBの一意索引は NULL の行を止められない（SQLite/PostgreSQL は NULL を別値扱い、SQL Server はフィルタで外す）ので、
/// 重複はこの判定で止める。
/// </para>
/// <para>
/// 同じ製造日・品目・工程では、作業区の範囲が重なる計画（作業区なしと作業区あり、上位の段と配下の段）を併存させない。
/// 予実は作業区違いの計画を合算するので、全体の計画と内訳の計画が並ぶと二重に数える。兄弟の作業区どうしは併存できる。
/// </para>
/// </summary>
public static class ProductionPlanPolicy
{
    /// <summary>計画のキー（作業区なしは null。タプルの比較で null どうしが一致する）</summary>
    public readonly record struct PlanKey(DateOnly BusinessDate, int ProductId, int ProcessId, int? WorkCenterId);

    public static PlanKey KeyOf(ProductionPlan plan) =>
        new(plan.BusinessDate, plan.ProductId, plan.ProcessId, plan.WorkCenterId);

    /// <summary>DB上で同じキーの計画を探す条件（<see cref="PlanKey"/> と同じ規則をSQLに落とす）</summary>
    public static Expression<Func<ProductionPlan, bool>> SameKey(PlanKey key) =>
        p => p.BusinessDate == key.BusinessDate
            && p.ProductId == key.ProductId
            && p.ProcessId == key.ProcessId
            && (key.WorkCenterId == null ? p.WorkCenterId == null : p.WorkCenterId == key.WorkCenterId);

    /// <summary>計画数量の確認。0 は計画上の休止として許し、負の値だけを弾く</summary>
    public static string? CheckQuantity(decimal plannedQuantity) =>
        plannedQuantity < 0
            ? ApiText.T("計画数量は0以上で指定してください（{0}）。", plannedQuantity)
            : null;

    /// <summary>同じキーの計画が既にあるときの理由</summary>
    public static string Duplicated(DateOnly businessDate, string productCode, string processCode) =>
        ApiText.T(
            "製造日 {0:yyyy-MM-dd} の品目 '{1}'・工程 '{2}' の計画は、同じ作業区で既に登録されています。",
            businessDate, productCode, processCode);

    /// <summary>
    /// 2つの作業区の範囲が重なるか。作業区なしは全体を表すのでどれとも重なり、
    /// 作業区ありどうしは同じか一方が他方の配下にあるときに重なる（兄弟の作業区は重ならない）
    /// </summary>
    public static bool Overlaps(int? a, int? b, IReadOnlyCollection<WorkCenter> allWorkCenters) =>
        a is not { } x || b is not { } y
        || WorkCenterHierarchyPolicy.SelfAndDescendantIds(x, allWorkCenters).Contains(y)
        || WorkCenterHierarchyPolicy.SelfAndDescendantIds(y, allWorkCenters).Contains(x);

    /// <summary>範囲の重なる計画が既にあるときの理由（作業区なしは "-" で示す）</summary>
    public static string Overlapped(
        DateOnly businessDate, string productCode, string processCode, string? workCenterCode, string? otherWorkCenterCode) =>
        ApiText.T(
            "製造日 {0:yyyy-MM-dd} の品目 '{1}'・工程 '{2}' には作業区 '{3}' の計画があり、作業区 '{4}' の計画と範囲が重なります（作業区なしと作業区あり、上位の段と配下の段の計画は、予実で二重に数えるため同時に登録できません）。",
            businessDate, productCode, processCode, otherWorkCenterCode ?? "-", workCenterCode ?? "-");

    /// <summary>判定の結果。<see cref="Conflict"/> はキーの重複・範囲の重なり（409）、それ以外は入力不正（400）</summary>
    public sealed record Violation(string Message, bool Conflict);

    /// <summary>
    /// 単票の登録・更新で、参照先の存在・計画数量・キーの重複をまとめて確かめる。
    /// CSVは参照先をコードで引いて存在を確かめ、同じキーは上書きするので、
    /// <see cref="CheckQuantity"/> と <see cref="KeyOf"/> を直接使う
    /// </summary>
    public static async Task<Violation?> CheckAsync(
        MesAppDbContext db, PlanKey key, decimal plannedQuantity, int? excludeId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == key.ProductId, ct);
        if (product is null)
        {
            return new(ApiText.T("対象品目（ID {0}）が見つかりません。", key.ProductId), false);
        }
        var process = await db.Processes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == key.ProcessId, ct);
        if (process is null)
        {
            return new(ApiText.T("対象工程（ID {0}）が見つかりません。", key.ProcessId), false);
        }
        if (key.WorkCenterId is { } wcId && !await db.WorkCenters.AnyAsync(w => w.Id == wcId, ct))
        {
            return new(ApiText.T("作業区（ID {0}）が見つかりません。", wcId), false);
        }
        if (CheckQuantity(plannedQuantity) is { } quantityError)
        {
            return new(quantityError, false);
        }

        var duplicated = await db.ProductionPlans
            .Where(SameKey(key))
            .AnyAsync(p => excludeId == null || p.Id != excludeId, ct);
        if (duplicated)
        {
            return new(Duplicated(key.BusinessDate, product.Code, process.Code), true);
        }

        // 同じ製造日・品目・工程で作業区の範囲が重なる計画は、予実（作業区で絞らない表示）で合算されて二重に数える
        var siblings = await db.ProductionPlans.AsNoTracking()
            .Where(p => p.BusinessDate == key.BusinessDate && p.ProductId == key.ProductId
                && p.ProcessId == key.ProcessId && (excludeId == null || p.Id != excludeId))
            .Select(p => p.WorkCenterId)
            .ToListAsync(ct);
        if (siblings.Count == 0)
        {
            return null;
        }
        var workCenters = await db.WorkCenters.AsNoTracking().ToListAsync(ct);
        foreach (var other in siblings.Where(other => Overlaps(key.WorkCenterId, other, workCenters)))
        {
            return new(Overlapped(key.BusinessDate, product.Code, process.Code,
                CodeOf(key.WorkCenterId, workCenters), CodeOf(other, workCenters)), true);
        }
        return null;
    }

    private static string? CodeOf(int? workCenterId, IReadOnlyCollection<WorkCenter> all) =>
        workCenterId is { } id ? all.FirstOrDefault(w => w.Id == id)?.Code : null;
}
