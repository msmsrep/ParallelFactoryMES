namespace MesApp.Core.Entities;

/// <summary>製造指図（Spec.md 5.2 ManufacturingOrder。A-20、B-70-10）</summary>
public class ManufacturingOrder
{
    public int Id { get; set; }

    /// <summary>指図番号（一意。自動採番：MOyyyyMMdd-連番）</summary>
    public string OrderNo { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>納期</summary>
    public DateOnly? DueDate { get; set; }

    public ManufacturingOrderType OrderType { get; set; } = ManufacturingOrderType.Normal;

    public ManufacturingOrderStatus Status { get; set; } = ManufacturingOrderStatus.Draft;

    /// <summary>承認者（A-20-20-01）</summary>
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>元指図（リワーク指図のみ。B-70-10）</summary>
    public int? SourceOrderId { get; set; }
    public ManufacturingOrder? SourceOrder { get; set; }

    /// <summary>産出ロット（工程展開時に採番。B-10-10-05）</summary>
    public int? OutputLotId { get; set; }
    public Lot? OutputLot { get; set; }

    /// <summary>備考（変更理由・突発指図の経緯など）</summary>
    public string? Note { get; set; }

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<WorkOrder> WorkOrders { get; set; } = [];
}

/// <summary>作業指示（Spec.md 5.2 WorkOrder。製造指図×工程。B-10）</summary>
public class WorkOrder
{
    public int Id { get; set; }

    /// <summary>指示番号（一意。指図番号-工程順序）</summary>
    public string WorkOrderNo { get; set; } = string.Empty;

    public int ManufacturingOrderId { get; set; }
    public ManufacturingOrder? ManufacturingOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int ProcessId { get; set; }
    public ProcessMaster? Process { get; set; }

    /// <summary>展開元の工順順序</summary>
    public int RoutingSequence { get; set; }

    public decimal PlannedQuantity { get; set; }

    /// <summary>着手順（差立で設定。B-10-20-03。初期リリースでは順序強制はしない：Spec.md 3.9）</summary>
    public int? DispatchOrder { get; set; }

    /// <summary>割当作業者（B-10-20-01。スキル照合 F-20-30-01 済み）</summary>
    public string? AssignedUserId { get; set; }
    public AppUser? AssignedUser { get; set; }

    /// <summary>割当設備（B-10-20-02）</summary>
    public int? AssignedEquipmentId { get; set; }
    public Equipment? AssignedEquipment { get; set; }

    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Created;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>ロット（Spec.md 5.3 Lot。Phase 2では産出ロット採番 B-10-10-05 のために先行導入）</summary>
public class Lot
{
    public int Id { get; set; }

    /// <summary>ロット番号（一意。自動採番：品目コード-製造日-連番、または手入力）</summary>
    public string LotNumber { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>初期数量（生産ロットは実績計上時に確定。採番時点では0）</summary>
    public decimal InitialQuantity { get; set; }

    public LotOriginType OriginType { get; set; }

    /// <summary>生成元作業指示（生産ロットのみ。完成実績計上時に設定：Phase 3）</summary>
    public int? SourceWorkOrderId { get; set; }
    public WorkOrder? SourceWorkOrder { get; set; }

    /// <summary>製造日/受入日（業務日付）</summary>
    public DateOnly? ManufacturedOn { get; set; }

    /// <summary>有効期限</summary>
    public DateOnly? ExpiresOn { get; set; }

    public LotStockStatus StockStatus { get; set; } = LotStockStatus.Normal;

    /// <summary>グレード（検査結果により出荷先・品目が変わる製品の管理。C-60-10-01）</summary>
    public string? Grade { get; set; }

    /// <summary>親ロット（分割・統合・振替の由来。系譜の保持）</summary>
    public int? ParentLotId { get; set; }
    public Lot? ParentLot { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
