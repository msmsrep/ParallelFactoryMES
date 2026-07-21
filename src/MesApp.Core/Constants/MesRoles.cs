namespace MesApp.Core.Constants;

/// <summary>
/// システムロール定義（Spec.md 1.2 対象ユーザーに対応）
/// </summary>
public static class MesRoles
{
    /// <summary>システム管理者（マスタ管理、ユーザー管理、シート・端末管理）</summary>
    public const string SystemAdmin = "SystemAdmin";

    /// <summary>生産管理担当者（製造指図発行・承認、進捗管理、MBOM/BOPマスタ管理）</summary>
    public const string ProductionManager = "ProductionManager";

    /// <summary>現場作業者（作業指示確認、段取り・実績・部材投入の記録）</summary>
    public const string Operator = "Operator";

    /// <summary>物流・倉庫担当者（受入、出庫・払出、出荷、棚卸）</summary>
    public const string Logistics = "Logistics";

    /// <summary>品質管理担当者（検査指示・実績・判定、不適合管理）</summary>
    public const string QualityControl = "QualityControl";

    /// <summary>品質保証担当者（出荷判定、トレーサビリティ）</summary>
    public const string QualityAssurance = "QualityAssurance";

    /// <summary>設備保全担当者（設備台帳、保全計画・指示・実績、治工具管理）</summary>
    public const string Maintenance = "Maintenance";

    public static readonly string[] All =
    [
        SystemAdmin,
        ProductionManager,
        Operator,
        Logistics,
        QualityControl,
        QualityAssurance,
        Maintenance,
    ];
}
