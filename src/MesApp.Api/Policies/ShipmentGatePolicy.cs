using System.Linq.Expressions;
using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 出荷実行の可否判定（Spec.md 3.9 ロットの使用可否。H-10-10、D-40-30）。
/// <para>
/// 出荷は「出荷判定の承認があること」と「出荷する現品が使える状態であること」の
/// 2つを同時に満たす必要がある。後者は <see cref="LotUsabilityPolicy"/> が担い、
/// このクラスは判定書ゲートの条件を1か所に定める。
/// </para>
/// </summary>
public static class ShipmentGatePolicy
{
    /// <summary>
    /// 出荷指示に対する有効な判定の条件（承認済みで、結果が可または特採）。
    /// EFで評価するためExpressionで返す
    /// </summary>
    public static Expression<Func<ShipmentJudgment, bool>> ValidJudgment(int shippingOrderId) =>
        j => j.ShippingOrderId == shippingOrderId
             && j.ApprovedAt != null
             && (j.Result == ShipmentJudgmentResult.Approved
                 || j.Result == ShipmentJudgmentResult.SpecialAcceptance);

    /// <summary>判定書ゲートを満たしていない場合の理由（満たしていればnull）</summary>
    public static string? CheckJudgment(bool hasApprovedJudgment) =>
        hasApprovedJudgment
            ? null
            : "承認済みの出荷判定（可または特採）がないため出荷できません（H-10-10）。";
}
