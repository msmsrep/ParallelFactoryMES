using MesApp.Api.Localization;

namespace MesApp.Api.Policies;

/// <summary>
/// マスタを無効化してよいかの判定（Spec.md 3.8 マスタのCSV一括入出力）。
/// <para>
/// 無効化は「これから使わせない」操作であって、既に参照している業務データを消すものではない。
/// 参照されたまま無効化すると、参照側から辿れない・選び直せないデータが残る。
/// </para>
/// 判定はマスタ画面（単票API）とマスタCSV取込の両方で必要になるため、ここへ集約する
/// （<see cref="LastAdminPolicy"/> と同じ理由。片方にだけ書くとCSVから迂回できてしまう）。
/// </summary>
public static class MasterDeactivationPolicy
{
    /// <summary>
    /// 作業手順書（I-30-40-01）。工順から参照されている手順書を無効化すると、
    /// 作業者が手順を辿れない作業指示ができる。
    /// </summary>
    /// <param name="referencingProductCodes">その手順書を参照している工順の品目コード（重複なし）</param>
    public static string? CheckWorkProcedure(
        string procedureNo, IReadOnlyCollection<string> referencingProductCodes) =>
        referencingProductCodes.Count == 0
            ? null
            : ApiText.T("手順書 '{0}' は品目 {1} の工順から参照されているため無効化できません。", procedureNo, string.Join("、", referencingProductCodes));

    /// <summary>
    /// 工程管理項目（B-30-30-04）。工順から紐付けている項目を無効化すると、
    /// 以降に展開する作業指示へ「使わせないはずの条件」が載り続ける（展開時は紐付けどおりに写すため）。
    /// </summary>
    /// <param name="referencingProductCodes">その項目を紐付けている工順の品目コード（重複なし）</param>
    public static string? CheckControlItem(
        string code, IReadOnlyCollection<string> referencingProductCodes) =>
        referencingProductCodes.Count == 0
            ? null
            : ApiText.T("工程管理項目 '{0}' は品目 {1} の工順から紐付けられているため無効化できません。", code, string.Join("、", referencingProductCodes));

    /// <summary>
    /// 勤務シフト（F-10-10-01）。所属する直として使われている間に無効化すると、従業員の所属が宙に浮く。
    /// </summary>
    /// <param name="assignedActiveUserCount">その直を所属する直としている在籍中の従業員数</param>
    public static string? CheckShift(string code, int assignedActiveUserCount) =>
        assignedActiveUserCount == 0
            ? null
            : ApiText.T("直 '{0}' は在籍中の従業員 {1} 名の所属になっているため無効化できません。", code, assignedActiveUserCount);
}
