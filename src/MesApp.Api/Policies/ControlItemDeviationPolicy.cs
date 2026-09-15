using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 製造条件の逸脱判定（Spec.md 5.7 製造条件の指示値と実績値。B-30-30-04）。
/// <para>
/// 判定の基準は工程管理項目マスタの現在値ではなく、<see cref="WorkOrderControlItem"/>
/// （指図展開時点のスナップショット）を使う。マスタは改訂され上書きされるため、
/// 現在値で判定すると同じ実績の合否が後から変わってしまう。検査の自動判定
/// （<c>InspectionOrdersController.Judge</c>）と同じ考え方。
/// </para>
/// </summary>
public static class ControlItemDeviationPolicy
{
    /// <summary>
    /// 許容範囲からの逸脱を判定する。true=逸脱、false=範囲内、null=判定しない。
    /// <para>
    /// 数値が無い（定性的な記録）か、上下限がどちらも無い（記録だけが目的の項目）ときは判定しない。
    /// 「判定できない」を「範囲内」と混同すると、条件を決めていない項目まで合格扱いになる。
    /// </para>
    /// </summary>
    public static bool? Judge(WorkOrderControlItem? instruction, decimal? numericValue)
    {
        if (instruction is null || numericValue is not { } value)
        {
            return null;
        }
        if (instruction.LowerLimit is null && instruction.UpperLimit is null)
        {
            return null;
        }
        var within = (instruction.LowerLimit is null || value >= instruction.LowerLimit)
                     && (instruction.UpperLimit is null || value <= instruction.UpperLimit);
        return !within;
    }

    /// <summary>逸脱の内容を日本語で説明する（記録・監査ログ・画面のメッセージ用）</summary>
    public static string Describe(WorkOrderControlItem instruction, decimal value)
    {
        var range = $"{instruction.LowerLimit?.ToString() ?? "-"}〜{instruction.UpperLimit?.ToString() ?? "-"}";
        return $"{instruction.ItemCode} {instruction.ItemName}: 実績 {value}{instruction.Unit} が" +
               $"許容範囲（{range}{instruction.Unit}）から外れています。";
    }
}
