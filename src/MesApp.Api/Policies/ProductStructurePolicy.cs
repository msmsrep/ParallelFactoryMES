using MesApp.Api.Localization;
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

    /// <summary>
    /// MBOMの代替部品グループの整合（A-40-10-04）。問題があれば日本語の理由を返す。
    /// <list type="bullet">
    /// <item>代替部品の行は代替部品グループを持つ（どの主材料の代わりかが決まらないと、投入の照合で意味を持たない）</item>
    /// <item>グループの主材料（代替部品でない行）はちょうど1つ。無いとバックフラッシュがそのグループの部材を何も引かず、
    /// 2つ以上だと代わりになるはずの部材を両方とも原単位ぶん引く（Spec.md 3.9）</item>
    /// </list>
    /// グループ名は前後の空白を除いて比べる（<see cref="NormalizeAlternativeGroup"/>）
    /// </summary>
    public static string? CheckAlternativeGroups(IEnumerable<(string ChildCode, string? Group, bool IsAlternative)> lines)
    {
        var normalized = lines.Select(l => (l.ChildCode, Group: NormalizeAlternativeGroup(l.Group), l.IsAlternative)).ToList();
        if (normalized.FirstOrDefault(l => l.IsAlternative && l.Group is null) is { ChildCode: { } orphan })
        {
            return ApiText.T("代替部品の行には代替部品グループが必要です（子品目 '{0}'）。", orphan);
        }
        foreach (var group in normalized.Where(l => l.Group is not null).GroupBy(l => l.Group!, StringComparer.Ordinal))
        {
            var primaries = group.Where(l => !l.IsAlternative).Select(l => l.ChildCode).ToList();
            if (primaries.Count == 0)
            {
                return ApiText.T("代替部品グループ '{0}' に主材料（代替部品でない行）がありません。", group.Key);
            }
            if (primaries.Count > 1)
            {
                return ApiText.T("代替部品グループ '{0}' に主材料が複数あります（{1}）。主材料は1つにし、他は代替部品にしてください。",
                    group.Key, string.Join(", ", primaries));
            }
        }
        return null;
    }

    /// <summary>代替部品グループ名の正規化（前後の空白を除き、空なら未指定）。保存とグループの比較で同じ形にする</summary>
    public static string? NormalizeAlternativeGroup(string? group) =>
        string.IsNullOrWhiteSpace(group) ? null : group.Trim();

    /// <summary>
    /// MBOMの循環（A→B→…→A）を探す（A-40-10-01）。親品目の明細を <paramref name="childIds"/> に置き換えたとき、
    /// 子品目から既存の明細をたどって親品目へ戻れるなら、その経路（親品目で始まり親品目で終わる品目IDの並び）を返す。
    /// 循環がなければ null。
    /// <para>
    /// 今の展開は1段だけなので循環があっても止まらないが、構成として作れない品目になる。
    /// 多段の所要量展開や逆展開を足したときに無限ループになるため、登録の時点で止める。
    /// <paramref name="childrenByParent"/> の親品目自身の明細は、置き換えられるので見ない
    /// </para>
    /// </summary>
    public static IReadOnlyList<int>? FindBomCycle(
        IReadOnlyDictionary<int, List<int>> childrenByParent, int parentId, IEnumerable<int> childIds)
    {
        // 幅優先でたどり、見つけた品目の手前を覚えておいて経路を組み立てる
        var previous = new Dictionary<int, int>();
        var queue = new Queue<int>();
        foreach (var child in childIds.Distinct())
        {
            if (child == parentId)
            {
                return [parentId, parentId];
            }
            previous[child] = parentId;
            queue.Enqueue(child);
        }
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == parentId || !childrenByParent.TryGetValue(current, out var next))
            {
                continue;
            }
            foreach (var child in next)
            {
                if (child == parentId)
                {
                    var path = new List<int> { parentId, current };
                    for (var at = current; previous[at] != parentId; at = previous[at])
                    {
                        path.Add(previous[at]);
                    }
                    path.Add(parentId);
                    // 末尾から組んだので、親品目→子品目→…→親品目の順に並べ直す
                    path.Reverse(1, path.Count - 2);
                    return path;
                }
                if (previous.TryAdd(child, current))
                {
                    queue.Enqueue(child);
                }
            }
        }
        return null;
    }
}
