using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 検査機の校正期限の判定（Spec.md 3.3・3.9。C-20-50-03）。
/// <para>
/// 校正期限を過ぎた機器の測定値は、規格に合っていても品質保証の根拠にならない。
/// 判定はここだけに置き、検査実績の登録と期限接近の一覧で同じ条件を使う
/// （片方だけを直すと「一覧では警告されないのに登録は弾かれる」ようなずれが起きる）。
/// </para>
/// </summary>
public static class InspectionDeviceCalibrationPolicy
{
    /// <summary>期限切れか（期限を設定していない機器は判定しない＝期限切れではない）</summary>
    public static bool IsExpired(DateOnly? calibrationDueOn, DateOnly businessDate) =>
        calibrationDueOn is { } due && due < businessDate;

    /// <summary>期限までの残り日数（期限なしはnull。負数は超過日数）</summary>
    public static int? DaysUntilDue(DateOnly? calibrationDueOn, DateOnly businessDate) =>
        calibrationDueOn is { } due ? due.DayNumber - businessDate.DayNumber : null;

    /// <summary>
    /// 検査実績に使えるか。使えない理由を日本語で返し、問題なければ null を返す。
    /// 無効化された機器も使わせない（現場から外した機器で測った記録が残らないようにする）
    /// </summary>
    public static string? CheckUsable(InspectionDevice device, DateOnly businessDate)
    {
        if (!device.IsActive)
        {
            return $"検査機 '{device.Code}' は無効化されています。";
        }
        if (IsExpired(device.CalibrationDueOn, businessDate))
        {
            return $"検査機 '{device.Code}' は校正期限（{device.CalibrationDueOn:yyyy-MM-dd}）を過ぎています。"
                   + "校正を実施してから検査実績を登録してください。";
        }
        return null;
    }
}
