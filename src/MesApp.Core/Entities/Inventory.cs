namespace MesApp.Core.Entities;

/// <summary>
/// 在庫（Spec.md 5.3 InventoryStock。ロット×ロケーション単位の現在数量。
/// 更新はConcurrencyStampによる楽観的同時実行制御：Spec.md 3.9・改訂7）
/// </summary>
public class InventoryStock
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int LotId { get; set; }
    public Lot? Lot { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>楽観的同時実行制御トークン（SQLiteでも動作するようGUID文字列を毎更新で書き換える）</summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}

/// <summary>在庫トランザクション（Spec.md 5.3 InventoryTransaction。全在庫増減の履歴）</summary>
public class InventoryTransaction
{
    public long Id { get; set; }

    public InventoryTransactionType Type { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int LotId { get; set; }
    public Lot? Lot { get; set; }

    /// <summary>数量（常に正。方向は区分と移動元/先で表す）</summary>
    public decimal Quantity { get; set; }

    /// <summary>移動元ロケーション（出庫・払出・移動元）</summary>
    public int? FromLocationId { get; set; }
    public Location? FromLocation { get; set; }

    /// <summary>移動先ロケーション（受入・入庫・移動先）</summary>
    public int? ToLocationId { get; set; }
    public Location? ToLocation { get; set; }

    /// <summary>関連作業指示（生産連動出庫・部材投入・在庫計上時）</summary>
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>関連ピッキング指示</summary>
    public int? PickingOrderId { get; set; }

    /// <summary>関連出荷指示</summary>
    public int? ShippingOrderId { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    public string? PerformedByUserId { get; set; }

    public string? Note { get; set; }
}

/// <summary>出庫・ピッキング指示（Spec.md 5.3 PickingOrder。D-20、D-40-20）</summary>
public class PickingOrder
{
    public int Id { get; set; }

    /// <summary>指示番号（一意。PKyyyyMMdd-連番）</summary>
    public string OrderNo { get; set; } = string.Empty;

    public PickingOrderType Type { get; set; }

    /// <summary>払出先の作業指示（工程払出時）</summary>
    public int? WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>払出先の出荷指示（出荷ピッキング時）</summary>
    public int? ShippingOrderId { get; set; }
    public ShippingOrder? ShippingOrder { get; set; }

    public PickingOrderStatus Status { get; set; } = PickingOrderStatus.Instructed;

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ExecutedByUserId { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }

    public List<PickingLine> Lines { get; set; } = [];
}

/// <summary>ピッキング明細（先入れ先出しのロット・ロケーション指定。D-20-10-02）</summary>
public class PickingLine
{
    public int Id { get; set; }

    public int PickingOrderId { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int LotId { get; set; }
    public Lot? Lot { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    public decimal Quantity { get; set; }
}

/// <summary>出荷指示（Spec.md 5.3 ShippingOrder。D-40-20〜30）</summary>
public class ShippingOrder
{
    public int Id { get; set; }

    /// <summary>出荷番号（一意。SHyyyyMMdd-連番）</summary>
    public string ShippingNo { get; set; } = string.Empty;

    /// <summary>出荷先</summary>
    public string Destination { get; set; } = string.Empty;

    /// <summary>出荷予定日</summary>
    public DateOnly? PlannedDate { get; set; }

    public ShippingOrderStatus Status { get; set; } = ShippingOrderStatus.Instructed;

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ShippedAt { get; set; }
    public string? ShippedByUserId { get; set; }

    public List<ShippingLine> Lines { get; set; } = [];
}

/// <summary>出荷明細（品目×数量）</summary>
public class ShippingLine
{
    public int Id { get; set; }

    public int ShippingOrderId { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>出荷済数量（出荷実行で更新）</summary>
    public decimal ShippedQuantity { get; set; }
}

/// <summary>棚卸（Spec.md 5.3 Stocktake。D-50-10）</summary>
public class Stocktake
{
    public int Id { get; set; }

    /// <summary>棚卸番号（一意。STyyyyMMdd-連番）</summary>
    public string StocktakeNo { get; set; } = string.Empty;

    /// <summary>対象ロケーション（null＝全ロケーション）</summary>
    public int? TargetLocationId { get; set; }
    public Location? TargetLocation { get; set; }

    public StocktakeStatus Status { get; set; } = StocktakeStatus.Instructed;

    public string? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? FinalizedAt { get; set; }
    public string? FinalizedByUserId { get; set; }

    public List<StocktakeLine> Lines { get; set; } = [];
}

/// <summary>棚卸明細（理論数量のスナップショット＋実棚数量）</summary>
public class StocktakeLine
{
    public int Id { get; set; }

    public int StocktakeId { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int LotId { get; set; }
    public Lot? Lot { get; set; }

    public int LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>理論数量（棚卸指示作成時点のスナップショット）</summary>
    public decimal TheoreticalQuantity { get; set; }

    /// <summary>実棚数量（D-50-10-02）</summary>
    public decimal? CountedQuantity { get; set; }

    /// <summary>差異調整済みか（確定時に設定）</summary>
    public bool IsAdjusted { get; set; }
}
