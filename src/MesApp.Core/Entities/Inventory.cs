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

/// <summary>
/// サンプル品の保管（Spec.md 5.3 SampleStorage。D-40-50-01）。
/// <para>
/// 検査で採取したサンプルは現物として残り、保管期限まで捨てられない。
/// 採取した時点で**在庫からは抜く**（保管棚へ移り、出荷・投入には使えないため。
/// 在庫に残すと引当・先入れ先出しの対象になってしまう）。
/// </para>
/// </summary>
public class SampleStorage
{
    public int Id { get; set; }

    /// <summary>サンプル番号（一意。自動採番：SPyyyyMMdd-連番）</summary>
    public string SampleNo { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>採取元ロット（どのロットのサンプルかを追えるようにする）</summary>
    public int LotId { get; set; }
    public Lot? Lot { get; set; }

    /// <summary>採取のきっかけとなった検査指示（無い運用もあるため任意）</summary>
    public int? InspectionOrderId { get; set; }
    public InspectionOrder? InspectionOrder { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>保管場所</summary>
    public int StorageLocationId { get; set; }
    public Location? StorageLocation { get; set; }

    /// <summary>採取日（業務日付）</summary>
    public DateOnly CollectedOn { get; set; }

    /// <summary>保管期限（この日までは捨てられない。未設定なら期限の判定を行わない）</summary>
    public DateOnly? RetainUntil { get; set; }

    public SampleStorageStatus Status { get; set; } = SampleStorageStatus.Stored;

    public string? CollectedByUserId { get; set; }

    /// <summary>保管終了（廃棄・払出）の日と実施者</summary>
    public DateOnly? ClosedOn { get; set; }
    public string? ClosedByUserId { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
