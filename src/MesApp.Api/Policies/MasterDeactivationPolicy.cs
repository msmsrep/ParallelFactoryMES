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
            : $"手順書 '{procedureNo}' は品目 {string.Join("、", referencingProductCodes)} の工順から" +
              "参照されているため無効化できません。";

    /// <summary>
    /// 勤務シフト（F-10-10-01）。所属する直として使われている間に無効化すると、従業員の所属が宙に浮く。
    /// </summary>
    /// <param name="assignedActiveUserCount">その直を所属する直としている在籍中の従業員数</param>
    public static string? CheckShift(string code, int assignedActiveUserCount) =>
        assignedActiveUserCount == 0
            ? null
            : $"直 '{code}' は在籍中の従業員 {assignedActiveUserCount} 名の所属になっているため無効化できません。";
}
