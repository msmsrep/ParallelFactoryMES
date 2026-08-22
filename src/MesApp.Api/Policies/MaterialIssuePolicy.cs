using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 部材投入の可否判定（Spec.md 3.2 部材投入。B-30-20）。
/// <para>
/// 誤投入は品質とトレーサビリティに直結するため、投入する部材が指図の予定材料
/// （<see cref="ManufacturingOrderMaterial"/>。展開時にMBOMから固定）に含まれることを照合する。
/// マスタの現在値ではなく予定材料を基準にするので、仕掛中の指図は途中のMBOM改訂に影響されない。
/// バックフラッシュは同じ予定材料から投入部材を導くため元から整合しており、
/// 手動投入だけがこの照合を必要とする。
/// </para>
/// ロット自体の使用可否（在庫ステータス・有効期限）は <see cref="LotUsabilityPolicy"/> が判定する。
/// </summary>
public static class MaterialIssuePolicy
{
    /// <summary>
    /// 投入部材と予定材料の照合。投入できない場合は日本語の理由を返す（可ならnull）。
    /// </summary>
    /// <param name="parentProductCode">作業指示の品目コード（＝MBOMの親品目）</param>
    /// <param name="plannedProductIds">指図の予定材料の品目ID。代替部品グループの各行も含む</param>
    /// <param name="material">投入しようとしている部材の品目</param>
    public static string? CheckAgainstBom(
        string parentProductCode, IReadOnlyCollection<int> plannedProductIds, Product material)
    {
        if (plannedProductIds.Count == 0)
        {
            return $"この指図には予定材料がありません（品目 '{parentProductCode}' のMBOMが未登録のまま展開されています）。" +
                   "MBOMを登録してから指図を展開し直してください。";
        }
        if (!plannedProductIds.Contains(material.Id))
        {
            return $"品目 '{material.Code}' は '{parentProductCode}' の予定材料に含まれないため投入できません" +
                   "（代替部品として使う場合はMBOMの代替部品グループへ登録し、指図を展開し直してください）。";
        }
        return null;
    }
}
