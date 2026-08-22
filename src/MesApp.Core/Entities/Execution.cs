namespace MesApp.Core.Entities;

/// <summary>段取り実績（Spec.md 5.2 SetupRecord。B-20-50 前段取り／B-40-40 後段取り）</summary>
public class SetupRecord
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public SetupType Type { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    public string PerformedByUserId { get; set; } = string.Empty;
    public AppUser? PerformedBy { get; set; }

    /// <summary>異常報告（B-20-50-05）</summary>
    public string? AbnormalityNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>チェックリスト実施記録（Spec.md 5.2 ChecklistRecord。B-30-10）</summary>
public class ChecklistRecord
{
    public int Id { get; set; }

    public int ChecklistId { get; set; }
    public Checklist? Checklist { get; set; }

    /// <summary>対象作業指示（設備点検などの場合はnull＋EquipmentId）</summary>
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>対象設備（始業前点検 B-20-10 等）</summary>
    public int? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    public string PerformedByUserId { get; set; } = string.Empty;
    public AppUser? PerformedBy { get; set; }

    public DateTimeOffset PerformedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ChecklistResultItem> Results { get; set; } = [];
}

/// <summary>チェックリスト項目別結果</summary>
public class ChecklistResultItem
{
    public int Id { get; set; }

    public int ChecklistRecordId { get; set; }

    public int ChecklistItemId { get; set; }
    public ChecklistItem? ChecklistItem { get; set; }

    public bool IsChecked { get; set; }

    public string? Note { get; set; }
}

/// <summary>部材投入実績（Spec.md 5.2 MaterialConsumption。B-30-20、B-40-10-08〜09）</summary>
public class MaterialConsumption
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>投入部材の品目</summary>
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>投入ロット（トレーサビリティの根拠。Spec.md 5.7）</summary>
    public int LotId { get; set; }
    public Lot? Lot { get; set; }

    /// <summary>払出元ロケーション</summary>
    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    public decimal Quantity { get; set; }

    public DateTimeOffset ConsumedAt { get; set; } = DateTimeOffset.UtcNow;

    public ConsumptionMethod Method { get; set; }

    public string? RecordedByUserId { get; set; }
}

/// <summary>生産実績（Spec.md 5.2 ProductionRecord。B-30-30、B-40-10）</summary>
public class ProductionRecord
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public string PerformedByUserId { get; set; } = string.Empty;
    public AppUser? PerformedBy { get; set; }

    /// <summary>良品数（B-40-10-01）</summary>
    public decimal GoodQuantity { get; set; }

    /// <summary>不良数</summary>
    public decimal DefectQuantity { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>産出ロット（最終工程の実績で設定。在庫計上 B-40-10-02 の対象）</summary>
    public int? OutputLotId { get; set; }
    public Lot? OutputLot { get; set; }

    /// <summary>入庫先ロケーション（最終工程のみ）</summary>
    public int? OutputLocationId { get; set; }

    /// <summary>承認状態（製造完了承認 B-40-10-10。作業指示の承認時に設定）</summary>
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 生産実績の訂正履歴（Spec.md 5.2 ProductionRecordCorrection。B-70-30-01）。
/// <para>
/// 実績は上書きで訂正されるため、訂正前の値がどこにも残らないと後から製造記録を説明できない。
/// 訂正のたびに1レコードを追加し、訂正前値・訂正後値・訂正者・訂正日時・訂正理由を業務履歴として残す。
/// 監査ログ（<see cref="AuditLog"/>）は操作の記録であり用途が異なる。こちらは製造記録・トレースから
/// 参照する業務データとして扱う。
/// </para>
/// </summary>
public class ProductionRecordCorrection
{
    public int Id { get; set; }

    public int ProductionRecordId { get; set; }
    public ProductionRecord? ProductionRecord { get; set; }

    /// <summary>訂正対象の作業指示（ロット・工程から履歴を引くための非正規化）</summary>
    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public decimal BeforeGoodQuantity { get; set; }
    public decimal BeforeDefectQuantity { get; set; }

    public decimal AfterGoodQuantity { get; set; }
    public decimal AfterDefectQuantity { get; set; }

    /// <summary>訂正理由（必須）</summary>
    public string Reason { get; set; } = string.Empty;

    public string? CorrectedByUserId { get; set; }
    public AppUser? CorrectedBy { get; set; }

    public DateTimeOffset CorrectedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>製造条件データ（Spec.md 5.2 ProductionDataRecord。B-30-30-04。手入力/CSV取込）</summary>
public class ProductionDataRecord
{
    public int Id { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>項目（温度・回転数等。工順の工程管理項目に対応）</summary>
    public string Item { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? RecordedByUserId { get; set; }
}

/// <summary>作業時間記録（Spec.md 5.2 WorkTimeRecord。B-30-30-02 直接／F-30-20-02 間接時間管理）</summary>
public class WorkTimeRecord
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }

    public WorkTimeType Type { get; set; }

    /// <summary>間接作業の内容（段取り・部材準備・設備メンテ等の自由記述）</summary>
    public string? IndirectCategory { get; set; }

    /// <summary>関連作業指示（直接作業時）</summary>
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    public string? Note { get; set; }
}

/// <summary>製造トラブル報告（Spec.md 5.2 TroubleReport。B-40-10-06、B-60-10-02〜04）</summary>
public class TroubleReport
{
    public int Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>区分（QCDS）</summary>
    public TroubleCategory Category { get; set; }

    /// <summary>対象作業指示</summary>
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>対象設備</summary>
    public int? EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    public string Content { get; set; } = string.Empty;

    /// <summary>対応履歴（追記式。B-60-10-04）</summary>
    public string? ResponseHistory { get; set; }

    public TroubleStatus Status { get; set; } = TroubleStatus.Open;

    public string ReportedByUserId { get; set; } = string.Empty;
    public AppUser? ReportedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>搬送・移動指示（Spec.md 5.2 TransferOrder。B-50-10、D-30-10-04）</summary>
public class TransferOrder
{
    public int Id { get; set; }

    public int LotId { get; set; }
    public Lot? Lot { get; set; }

    public decimal Quantity { get; set; }

    public int FromLocationId { get; set; }
    public Location? FromLocation { get; set; }

    public int ToLocationId { get; set; }
    public Location? ToLocation { get; set; }

    public TransferOrderStatus Status { get; set; } = TransferOrderStatus.Instructed;

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ExecutedByUserId { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
}
