using MesApp.Core.Abstractions;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api;

/// <summary>
/// マスタの無効化（論理削除）の定型処理。
/// <para>
/// 「存在確認 → 無効化してよいかの判定 → IsActive を false → 保存 → 監査ログ」という並びは
/// 全マスタで同じで、違うのは監査ログに載せる識別子と、マスタ固有の判定だけ。
/// コントローラごとに書き分けると、監査ログの区分や detail の形が少しずつずれる。
/// </para>
/// 判定そのものは <see cref="Policies.MasterDeactivationPolicy"/> 側に置く
/// （CSV取込と同じ条件・同じ文面で弾くため）。ここはその結果を応答に変換するだけ。
/// </summary>
public static class MasterDeactivationExtensions
{
    /// <typeparam name="TEntity">対象のマスタ。監査ログの対象名には型名を使う</typeparam>
    /// <param name="describe">監査ログの detail に載せる識別子（例: <c>l => $"code={l.Code}"</c>）</param>
    /// <param name="precheck">
    /// マスタ固有の判定。理由を返すと 409（Conflict）にして無効化しない
    /// </param>
    /// <param name="onDeactivating">無効化と同時に触る項目（更新日時など）があるマスタ用</param>
    /// <param name="category">監査ログの区分。既定は "Master"</param>
    /// <param name="action">監査ログの操作名。既定は "Deactivate"</param>
    public static async Task<IActionResult> DeactivateMasterAsync<TEntity>(
        this ControllerBase controller,
        MesAppDbContext db,
        IAuditLogger auditLogger,
        int id,
        Func<TEntity, string> describe,
        CancellationToken ct,
        Func<TEntity, Task<string?>>? precheck = null,
        Action<TEntity>? onDeactivating = null,
        string category = "Master",
        string action = "Deactivate")
        where TEntity : class, IDeactivatableMaster
    {
        var entity = await db.Set<TEntity>().FindAsync([id], ct);
        if (entity is null)
        {
            return controller.NotFound();
        }
        if (precheck is not null && await precheck(entity) is { } reason)
        {
            return controller.ConflictProblem(reason);
        }
        entity.IsActive = false;
        onDeactivating?.Invoke(entity);
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync(category, action, typeof(TEntity).Name, id.ToString(),
            detail: describe(entity), ct: ct);
        return controller.NoContent();
    }
}
