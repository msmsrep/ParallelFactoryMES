using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 品目マスタ・工順（BOP）から参照できるマスタの条件（Spec.md 5.7。A-40-10 / A-40-20）。
/// <para>
/// 単票API（<c>ProductStructureService</c> / <c>ProductsController</c>）とマスタCSV取込（<c>MasterCsvService</c>）の
/// 両方から同じ条件で呼ぶ。経路ごとに条件を持たせると、片方だけ直したときに
/// フォームからは紐付けられない作業区・手順書がCSVからは紐付けられてしまう（Spec.md 7.4）。
/// </para>
/// </summary>
public static class ProductStructurePolicy
{
    /// <summary>
    /// 工順に紐付けられる作業区。最下段かつ有効なものに限る
    /// （設備と同じ理由。上位の段に付けると作業区別の集計軸が定まらない）
    /// </summary>
    public static IQueryable<WorkCenter> AssignableWorkCenters(IQueryable<WorkCenter> source) =>
        source.Where(w => w.Level == WorkCenterLevel.WorkCenter && w.IsActive);

    /// <summary>工順に紐付けられる作業手順書。無効な手順書を紐付けると、作業者が改訂前の手順で作業してしまう</summary>
    public static IQueryable<WorkProcedure> AssignableWorkProcedures(IQueryable<WorkProcedure> source) =>
        source.Where(p => p.IsActive);

    /// <summary>品目の既定ロケーションに指定できるロケーション。無効なロケーションは推奨に出せないため弾く</summary>
    public static IQueryable<Location> AssignableDefaultLocations(IQueryable<Location> source) =>
        source.Where(l => l.IsActive);

    /// <summary>
    /// 工順の候補設備。代表設備（EquipmentId）も候補に含める。
    /// 候補を書かずに代表だけ指定した既存の工順が、差立で設備を選べなくならないようにするため
    /// </summary>
    public static IEnumerable<int> CandidateEquipmentIds(IEnumerable<int>? candidates, int? representative) =>
        (candidates ?? [])
            .Concat(representative is { } id ? [id] : [])
            .Distinct();
}
