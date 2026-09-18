using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 治工具の引当可否（Spec.md 3.2。B-20-30-01）。
/// <para>
/// 引当（確保）と払出（受領）の両方から同じ条件で判定する。
/// 引当時は使えた治工具が、払出までの間に寿命へ達したりメンテへ入ったりしうるため、
/// 渡す直前にもう一度見る（片方だけに置くと、寿命到達後の現物が現場へ出てしまう）。
/// </para>
/// </summary>
public static class ToolIssuePolicy
{
    /// <summary>
    /// 使える治工具か。使えない理由を日本語で返し、問題なければ null を返す。
    /// </summary>
    /// <param name="tool">対象の治工具</param>
    /// <param name="isLifeReached">寿命閾値に達しているか（E-60-20-02 の判定結果）</param>
    /// <param name="hasOpenIssue">他の作業指示に引当中（未返却）か</param>
    public static string? CheckIssuable(Tool tool, bool isLifeReached, bool hasOpenIssue)
    {
        if (!tool.IsActive)
        {
            return $"治工具 '{tool.Code}' は無効化されています。";
        }
        if (tool.Status == ToolStatus.Retired)
        {
            return $"治工具 '{tool.Code}' は廃棄済みです。";
        }
        if (tool.Status == ToolStatus.UnderMaintenance)
        {
            return $"治工具 '{tool.Code}' はメンテナンス中です。";
        }
        if (isLifeReached)
        {
            return $"治工具 '{tool.Code}' は寿命に達しています。交換してから引き当ててください。";
        }
        if (hasOpenIssue)
        {
            return $"治工具 '{tool.Code}' は他の作業指示に引当中です。返却されてから引き当ててください。";
        }
        return null;
    }

    /// <summary>
    /// マスタ編集（画面・CSV取込）で状態を変えてよいか。変えられない理由を日本語で返し、問題なければ null を返す。
    /// <para>
    /// 使用中は引当（<see cref="ToolIssue"/>）から決まる状態なので手で付け外しさせない。
    /// 手で使用中にすると引当の無い治工具が使用中に見え、引当中のものを使用可能に戻すと
    /// 現物が現場にあるのに空いているように見える。引当の無い治工具を使用中から戻すのは、
    /// ずれた表示を直す操作として認める。メンテナンス中・廃棄への変更は引当中でも認める
    /// （払出時にもう一度判定するため、現物は現場へ出ない）。
    /// </para>
    /// </summary>
    /// <param name="code">治工具コード（メッセージ用）</param>
    /// <param name="current">現在の状態（新規登録時は null）</param>
    /// <param name="requested">変更後の状態</param>
    /// <param name="hasOpenIssue">引当中（未返却）の引当があるか</param>
    public static string? CheckManualStatus(string code, ToolStatus? current, ToolStatus requested, bool hasOpenIssue)
    {
        if (requested == ToolStatus.InUse && current != ToolStatus.InUse)
        {
            return $"治工具 '{code}' を手で使用中にはできません。使用中は作業指示への引当で設定されます。";
        }
        if (requested == ToolStatus.Available && hasOpenIssue)
        {
            return $"治工具 '{code}' は作業指示に引当中のため使用可能にできません。返却または引当の取消をしてください。";
        }
        return null;
    }

    /// <summary>
    /// 寿命に達しているか（E-60-20-02 と同じ条件）。
    /// 閾値を設定していない治工具は判定しない（基準が無いため寿命到達にはしない）
    /// </summary>
    public static bool IsLifeReached(Tool tool, int cumulativeCount, decimal cumulativeHours) =>
        (tool.LifeThresholdCount is > 0 && cumulativeCount >= tool.LifeThresholdCount.Value)
        || (tool.LifeThresholdHours is > 0 && cumulativeHours >= tool.LifeThresholdHours.Value);
}
