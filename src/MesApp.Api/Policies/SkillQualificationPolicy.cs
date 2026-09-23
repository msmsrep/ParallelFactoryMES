using MesApp.Api.Localization;
using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 作業者のスキル・資格照合（Spec.md 3.6。F-20-30-01）。
/// <para>
/// 差立（作業員割当）と着手の両方から同じ条件で判定する。差立は省略できる（小規模運用）うえ、
/// 割り当てた者と実際に着手する者が違うことも、差立から着手までに有効期限が切れることもある。
/// 差立にだけ置くと、必要スキルを持たない者がそのまま作業できてしまう。
/// </para>
/// </summary>
public static class SkillQualificationPolicy
{
    /// <summary>
    /// 必要スキルを満たすか。満たさない理由を日本語で返し、問題なければ null を返す。
    /// </summary>
    /// <param name="userDisplayName">作業者の表示名（メッセージ用）</param>
    /// <param name="skill">必要スキル</param>
    /// <param name="userSkill">作業者の保有スキル（未保有は null）</param>
    /// <param name="businessDate">判定する製造日（業務日付）</param>
    public static string? Check(string userDisplayName, SkillMaster skill, UserSkill? userSkill, DateOnly businessDate)
    {
        if (userSkill is null)
        {
            return ApiText.T("作業者 '{0}' は必要スキル '{1}' を保有していません。", userDisplayName, skill.Name);
        }
        if (skill.RequiresExpiry && (userSkill.ExpiresOn is null || userSkill.ExpiresOn < businessDate))
        {
            return ApiText.T("作業者 '{0}' のスキル '{1}' は有効期限切れです。", userDisplayName, skill.Name);
        }
        return null;
    }
}
