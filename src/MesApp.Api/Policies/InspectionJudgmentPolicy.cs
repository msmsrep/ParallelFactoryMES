using MesApp.Api.Localization;
using MesApp.Core.Entities;
using MesApp.Core.Localization;

namespace MesApp.Api.Policies;

/// <summary>
/// 検査実績の合否と総合判定の条件（Spec.md 5.7 検査の判定。C-20-10-04）。
/// 基準はマスタの現在値ではなく、指示発行時点のスナップショット（<see cref="InspectionOrderItem"/>）を使う。
/// 実績の登録と訂正の両方から呼ぶ（訂正だけ別の条件にすると、訂正が規格外品を合格にする抜け道になる）。
/// </summary>
public static class InspectionJudgmentPolicy
{
    /// <summary>
    /// 1件の実績の合否を決める。決められなければ日本語の理由を返す。
    /// <list type="bullet">
    /// <item>測定値があり規格値（下限・上限のどちらか）があれば、規格値で自動判定する（下限≦測定値≦上限）。
    /// 手で指定した合否が自動判定と食い違えば拒否する。規格外の品を使うなら不適合の特採で処置する（C-30）</item>
    /// <item>自動判定できない項目（規格値が無い・測定値が無い）は、手で指定した合否を使う（指定が無ければ拒否）</item>
    /// </list>
    /// </summary>
    public static (InspectionJudgment? Judgment, string? Error) Resolve(
        InspectionOrderItem item, decimal? measuredValue, InspectionJudgment? explicitJudgment)
    {
        if (measuredValue is { } measured && (item.LowerLimit is not null || item.UpperLimit is not null))
        {
            var pass = (item.LowerLimit is null || measured >= item.LowerLimit)
                       && (item.UpperLimit is null || measured <= item.UpperLimit);
            var auto = pass ? InspectionJudgment.Pass : InspectionJudgment.Fail;
            if (explicitJudgment is { } manual && manual != auto)
            {
                return (null, ApiText.T(
                    "検査項目 '{0}' の測定値 {1} は規格値による判定が「{2}」です。判定を手で変えることはできません（規格外の品を使う場合は不適合の特採で処置してください）。",
                    item.ItemCode, measured, EnumLabels.Of(auto)));
            }
            return (auto, null);
        }
        return explicitJudgment is null
            ? (null, ApiText.T("検査項目 '{0}' は規格値による自動判定ができません。judgmentを指定してください。", item.ItemCode))
            : (explicitJudgment, null);
    }

    /// <summary>
    /// 総合判定の前提。全項目に実績があり、サンプリング数を持つ項目はその数以上のサンプル（サンプル番号の種類）が
    /// そろっていること。満たさなければ日本語の理由を返す。サンプリング数は下限として扱う（多く測るのは妨げない）
    /// </summary>
    public static string? CheckReadyToJudge(
        IReadOnlyCollection<InspectionOrderItem> items, IReadOnlyCollection<InspectionResult> results)
    {
        var withoutResult = items.Count(i => results.All(r => r.InspectionItemId != i.InspectionItemId));
        if (withoutResult > 0)
        {
            return ApiText.T("実績未登録の検査項目が {0} 件あります。全項目の実績登録後に判定してください。", withoutResult);
        }
        foreach (var item in items.Where(i => i.SamplingCount is > 0).OrderBy(i => i.ItemCode, StringComparer.Ordinal))
        {
            var samples = results.Where(r => r.InspectionItemId == item.InspectionItemId)
                .Select(r => r.SampleNo).Distinct().Count();
            if (samples < item.SamplingCount)
            {
                return ApiText.T("検査項目 '{0}' はサンプリング数 {1} に対して実績が {2} サンプルです。サンプリング数以上の実績を登録してから判定してください。",
                    item.ItemCode, item.SamplingCount!.Value, samples);
            }
        }
        return null;
    }
}
