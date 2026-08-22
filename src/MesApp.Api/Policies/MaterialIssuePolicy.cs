using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 部材投入の可否判定（Spec.md 3.2 部材投入。B-30-20）。
/// <para>
/// 誤投入は品質とトレーサビリティに直結するため、投入する部材が作業指示の品目のMBOM
/// （<see cref="BomItem"/>）に定義された部材であることを照合する。バックフラッシュは
/// MBOMから投入部材を導くため元から整合しており、手動投入だけがこの照合を必要とする。
/// </para>
/// ロット自体の使用可否（在庫ステータス・有効期限）は <see cref="LotUsabilityPolicy"/> が判定する。
/// </summary>
public static class MaterialIssuePolicy
{
    /// <summary>
    /// 投入部材とMBOMの照合。投入できない場合は日本語の理由を返す（可ならnull）。
    /// </summary>
    /// <param name="parentProductCode">作業指示の品目コード（＝MBOMの親品目）</param>
    /// <param name="bomChildProductIds">MBOMに定義された部材の品目ID。代替部品グループの各行も含む</param>
    /// <param name="material">投入しようとしている部材の品目</param>
    public static string? CheckAgainstBom(
        string parentProductCode, IReadOnlyCollection<int> bomChildProductIds, Product material)
    {
        if (bomChildProductIds.Count == 0)
        {
            return $"品目 '{parentProductCode}' にMBOMが登録されていないため、投入部材を照合できません。" +
                   "MBOMを登録してから投入してください。";
        }
        if (!bomChildProductIds.Contains(material.Id))
        {
            return $"品目 '{material.Code}' は '{parentProductCode}' のMBOMに含まれないため投入できません" +
                   "（代替部品として使う場合はMBOMの代替部品グループへ登録してください）。";
        }
        return null;
    }
}
