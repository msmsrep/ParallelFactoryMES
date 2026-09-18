using MesApp.Core.Constants;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Identity;

namespace MesApp.Api.Policies;

/// <summary>
/// システム管理者を全滅させないための判定（F-10-10）。
/// <para>
/// 有効なシステム管理者がいなくなると、ユーザー管理も権限付与もできなくなる。
/// 初期セットアップAPIはUserテーブルが0件のときしか動かない（Spec.md 2.2 E）ため、
/// この状態からはDBを直接操作する以外に復旧できない。単独PC配布（Spec.md 7.8）では
/// 管理者が1人だけの運用が普通なので、現実に起こりうる。
/// </para>
/// 判定はユーザー管理画面とマスタCSV取込の両方で必要になるため、ここへ集約する。
/// </summary>
public static class LastAdminPolicy
{
    /// <summary>禁止する理由（日本語のProblemDetails・CSVエラーの双方で使う）</summary>
    public const string NoAdminRemains =
        "有効なシステム管理者がいなくなるため、この変更はできません。"
        + "先に別のユーザーへシステム管理者ロールを割り当ててください。";

    /// <summary>
    /// 1人分の変更で最後の管理者が失われないかを確認する。変更できない場合は理由を返す（可ならnull）。
    /// </summary>
    /// <param name="target">変更対象のユーザー（まだ変更を適用していない状態）</param>
    /// <param name="willBeActive">変更後の有効/無効</param>
    /// <param name="willHaveRoles">変更後のロール</param>
    public static async Task<string?> CheckUpdateAsync(
        UserManager<AppUser> userManager, AppUser target, bool willBeActive, IEnumerable<string> willHaveRoles)
    {
        // 変更後も本人が有効な管理者のままなら人数は減らない
        if (willBeActive && willHaveRoles.Contains(MesRoles.SystemAdmin))
        {
            return null;
        }
        // もともと有効な管理者でなければ、この変更で人数は減らない
        if (!target.IsActive || !await userManager.IsInRoleAsync(target, MesRoles.SystemAdmin))
        {
            return null;
        }

        var admins = await userManager.GetUsersInRoleAsync(MesRoles.SystemAdmin);
        return admins.Any(u => u.IsActive && u.Id != target.Id) ? null : NoAdminRemains;
    }

    /// <summary>
    /// 有効なシステム管理者が1人以上いるか。
    /// CSV一括取込のように複数行をまとめて適用する経路では、行ごとに見ると
    /// 「Aを降格してからBを昇格する」順序を誤って弾くため、全行の適用後にこちらで確認する。
    /// </summary>
    public static async Task<bool> HasActiveAdminAsync(UserManager<AppUser> userManager) =>
        (await userManager.GetUsersInRoleAsync(MesRoles.SystemAdmin)).Any(u => u.IsActive);
}
