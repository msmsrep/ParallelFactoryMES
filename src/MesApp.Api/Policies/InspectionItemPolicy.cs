using System.Linq.Expressions;
using MesApp.Api.Localization;
using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 検査基準（<see cref="InspectionItem"/>）の対象の決め方（Spec.md 5.7 検査基準の対象。C-10-10-02、C-20）。
/// <para>
/// 基準は品質管理が「品目単位・工程単位」で策定するもの（C-10-10-02）で、工順（BOP）には紐付けない
/// （受入・完成品・サンプル検査は工程を持たず、購入品には工順そのものが無い）。
/// 対象品目・対象工程はそれぞれ「空なら条件にしない、指定があれば一致を求める」とし、**両方を『かつ』で組み合わせる**。
/// 「または」で選ぶと、品目Yの加熱工程の基準が品目Xの加熱工程の検査に載り、品目Xの別工程の基準も載ってしまう。
/// </para>
/// 登録（単票APIとマスタCSV）と検査指示の発行（単票APIと実績CSV）の両方から呼ぶ。
/// </summary>
public static class InspectionItemPolicy
{
    /// <summary>
    /// 基準の定義の整合。問題があれば日本語の理由を返す。
    /// 対象工程を持てるのは工程内検査の基準だけ（他の検査は工程を持たないため、工程を指定した基準は永久に選ばれない）
    /// </summary>
    public static string? CheckDefinition(
        InspectionType type, int? targetProductId, int? targetProcessId,
        decimal? lowerLimit, decimal? upperLimit, decimal? standardValue)
    {
        if (lowerLimit is not null && upperLimit is not null && lowerLimit > upperLimit)
        {
            return ApiText.T("規格値の下限が上限を超えています。");
        }
        // 基準値が規格の外にあると、基準どおりの品が不合格と判定される
        if (standardValue is { } standard
            && ((lowerLimit is { } lower && standard < lower) || (upperLimit is { } upper && standard > upper)))
        {
            return ApiText.T("基準値が規格値の範囲の外にあります。");
        }
        if (type == InspectionType.InProcess)
        {
            // 対象を持たない基準はどの検査にも選ばれない（全品目・全工程に載せる基準は意図せず広がるため作らせない）
            return targetProductId is null && targetProcessId is null
                ? ApiText.T("工程内検査の基準には対象品目か対象工程を指定してください。")
                : null;
        }
        if (targetProcessId is not null)
        {
            return ApiText.T("対象工程を指定できるのは工程内検査の基準だけです。");
        }
        return targetProductId is null
            ? ApiText.T("受入・完成品・サンプル検査の基準には対象品目を指定してください。")
            : null;
    }

    /// <summary>検査指示の種別に対応する基準の種別（再検査は完成品の基準を使う）</summary>
    public static InspectionType ItemTypeOf(InspectionOrderType orderType) => orderType switch
    {
        InspectionOrderType.Receiving => InspectionType.Receiving,
        InspectionOrderType.InProcess => InspectionType.InProcess,
        InspectionOrderType.Sample => InspectionType.Sample,
        _ => InspectionType.FinalProduct,
    };

    /// <summary>
    /// その検査（種別・品目・工程）に使える基準。有効で、種別が同じで、対象品目・対象工程が「かつ」で合うもの。
    /// 工程を持たない検査（<paramref name="processId"/> が null）では、対象工程を持つ基準は選ばれない
    /// </summary>
    public static Expression<Func<InspectionItem, bool>> Applicable(
        InspectionType type, int productId, int? processId) =>
        i => i.IsActive && i.Type == type
             && (i.TargetProductId == null || i.TargetProductId == productId)
             && (i.TargetProcessId == null || i.TargetProcessId == processId)
             && (i.TargetProductId != null || i.TargetProcessId != null);
}
