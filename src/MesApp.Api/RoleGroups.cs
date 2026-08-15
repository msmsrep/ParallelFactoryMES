using MesApp.Core.Constants;

namespace MesApp.Api;

/// <summary>
/// エンドポイント用のロールグループ（Spec.md 7.4：RBACはAPI側で一元判定）。
/// 参照系は認証済みユーザー全員に開放し、更新系のみロールで絞る。
/// </summary>
public static class RoleGroups
{
    /// <summary>マスタ更新（マスタ管理は生産管理担当者とシステム管理者）</summary>
    public const string MasterWrite = $"{MesRoles.SystemAdmin},{MesRoles.ProductionManager}";

    /// <summary>製造指図の発行・変更・展開・差立・承認（Spec.md 3.9：単段階承認）</summary>
    public const string ProductionManage = $"{MesRoles.SystemAdmin},{MesRoles.ProductionManager}";

    /// <summary>ユーザー・スキル資格の管理（システム管理者専用）</summary>
    public const string UserAdmin = MesRoles.SystemAdmin;

    /// <summary>在庫・物流オペレーション（受入・在庫操作・出庫/払出・出荷・棚卸）</summary>
    public const string InventoryManage =
        $"{MesRoles.SystemAdmin},{MesRoles.ProductionManager},{MesRoles.Logistics}";
}
