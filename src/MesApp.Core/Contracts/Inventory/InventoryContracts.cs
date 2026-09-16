using System.ComponentModel.DataAnnotations;
using MesApp.Core.Entities;

namespace MesApp.Core.Contracts.Inventory;

// ---- ロット・在庫照会 ----

public record LotResponse(
    int Id, string LotNumber, int ProductId, string ProductCode, string ProductName,
    decimal InitialQuantity, LotOriginType OriginType, LotStockStatus StockStatus,
    DateOnly? ManufacturedOn, DateOnly? ExpiresOn, string? Grade, int? ParentLotId);

public record StockResponse(
    int Id, int ProductId, string ProductCode, string ProductName,
    int LotId, string LotNumber, LotStockStatus LotStatus, DateOnly? ExpiresOn,
    int LocationId, string LocationCode, decimal Quantity);

public record TransactionResponse(
    long Id, InventoryTransactionType Type, int ProductId, string ProductCode,
    int LotId, string LotNumber, decimal Quantity,
    int? FromLocationId, string? FromLocationCode, int? ToLocationId, string? ToLocationCode,
    int? WorkOrderId, DateTimeOffset Timestamp, string? Note);

// ---- 受入（D-10-10）----

public record ReceivingRequest(
    int ProductId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    int LocationId,
    /// <summary>受入ロット番号（未指定なら自動採番 D-10-10-02）</summary>
    string? LotNumber,
    DateOnly? ExpiresOn,
    string? Note);

// ---- 在庫オペレーション（D-10-30、D-30-10、D-40-40）----

public record MoveRequest(
    int LotId, int FromLocationId, int ToLocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity);

/// <summary>数量調整（実在庫との差異訂正 D-10-30-04。理由必須）</summary>
public record AdjustRequest(
    int LotId, int LocationId,
    [Range(0, double.MaxValue)] decimal NewQuantity,
    [Required] string Reason);

/// <summary>在庫ステータス変更（保留・廃棄予定・不良・検査待ち等。D-10-30-08）</summary>
public record LotStatusRequest(int LotId, LotStockStatus Status, string? Reason);

/// <summary>ロット分割（D-10-30-05。新ロットは親ロットの系譜を保持）</summary>
public record SplitRequest(
    int LotId, int LocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    string? NewLotNumber);

/// <summary>ロット統合（D-10-30-05。同一品目のロットを統合）</summary>
public record MergeRequest(int SourceLotId, int TargetLotId, int LocationId);

/// <summary>品目振替・ロット振替（D-10-30-06〜07。新品目IDか新ロット番号の少なくとも一方を指定）</summary>
public record LotTransferRequest(
    int LotId, int LocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    int? NewProductId,
    string? NewLotNumber);

/// <summary>在庫廃棄（D-50-30-01）</summary>
public record DiscardRequest(
    int LotId, int LocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    string? Reason);

/// <summary>返品（D-10-10-05）</summary>
public record ReturnRequest(
    int LotId, int LocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    string? Reason);

/// <summary>払出戻し（D-20-20-03）</summary>
public record IssueReturnRequest(
    int LotId, int LocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    int? WorkOrderId);

// ---- 出庫・ピッキング（D-20、D-40-20）----

public record PickingRequestLine(
    int ProductId,
    [Range(0.000001, double.MaxValue)] decimal Quantity);

/// <summary>
/// ピッキング指示の作成（D-20-10-01〜02）。明細のロット・ロケーションは
/// 先入れ先出し（有効期限優先）で自動引当される。
/// </summary>
public record PickingOrderCreateRequest(
    PickingOrderType Type,
    /// <summary>払出先の作業指示（工程払出時に必須）</summary>
    int? WorkOrderId,
    /// <summary>払出先の出荷指示（出荷ピッキング時に必須）</summary>
    int? ShippingOrderId,
    List<PickingRequestLine> Lines);

public record PickingLineResponse(
    int Id, int ProductId, string ProductCode, string ProductName,
    int LotId, string LotNumber, int LocationId, string LocationCode, decimal Quantity);

public record PickingOrderResponse(
    int Id, string OrderNo, PickingOrderType Type, PickingOrderStatus Status,
    int? WorkOrderId, string? WorkOrderNo, int? ShippingOrderId, string? ShippingNo,
    DateTimeOffset CreatedAt, DateTimeOffset? ExecutedAt,
    List<PickingLineResponse> Lines);

// ---- 出荷（D-40-20〜30）----

public record ShippingLineRequest(
    int ProductId,
    [Range(0.000001, double.MaxValue)] decimal Quantity);

public record ShippingOrderCreateRequest(
    [Required, MaxLength(200)] string Destination,
    DateOnly? PlannedDate,
    List<ShippingLineRequest> Lines);

/// <summary>出荷実行明細（出荷するロット・ロケーション・数量）</summary>
public record ShipLineRequest(
    int LotId, int LocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity);

public record ShipExecuteRequest(List<ShipLineRequest> Lines);

public record ShippingLineResponse(
    int Id, int ProductId, string ProductCode, string ProductName,
    decimal Quantity, decimal ShippedQuantity);

public record ShippingOrderResponse(
    int Id, string ShippingNo, string Destination, DateOnly? PlannedDate,
    ShippingOrderStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ShippedAt,
    List<ShippingLineResponse> Lines);

// ---- 棚卸（D-50-10）----

public record StocktakeCreateRequest(
    /// <summary>対象ロケーション（null＝全ロケーション）</summary>
    int? TargetLocationId);

public record StocktakeCountLine(int LineId, [Range(0, double.MaxValue)] decimal CountedQuantity);

/// <summary>実棚数登録（D-50-10-02）</summary>
public record StocktakeCountRequest(List<StocktakeCountLine> Counts);

public record StocktakeLineResponse(
    int Id, int ProductId, string ProductCode, string ProductName,
    int LotId, string LotNumber, int LocationId, string LocationCode,
    decimal TheoreticalQuantity, decimal? CountedQuantity,
    /// <summary>差異（実棚−理論。未入力はnull。D-50-10-03）</summary>
    decimal? Difference,
    bool IsAdjusted);

public record StocktakeResponse(
    int Id, string StocktakeNo, int? TargetLocationId, StocktakeStatus Status,
    DateTimeOffset CreatedAt, DateTimeOffset? FinalizedAt,
    List<StocktakeLineResponse> Lines);

// ---- サンプル品保管（D-40-50-01）----

/// <summary>サンプルの採取登録。採取した分は在庫から抜く（保管棚へ移り、出荷・投入には使えないため）</summary>
public record SampleCollectRequest(
    int LotId,
    int StorageLocationId,
    [Range(0.000001, double.MaxValue)] decimal Quantity,
    /// <summary>保管期限（未指定なら期限の判定を行わない）</summary>
    DateOnly? RetainUntil,
    int? InspectionOrderId,
    [MaxLength(500)] string? Note);

/// <summary>保管の終了（払出・廃棄）。どちらかを明示させる（黙って消えると保管の証跡が残らない）</summary>
public record SampleCloseRequest(
    SampleStorageStatus Status,
    [MaxLength(500)] string? Note);

public record SampleStorageResponse(
    int Id, string SampleNo,
    int ProductId, string ProductCode, string ProductName, string Unit,
    int LotId, string LotNumber,
    int? InspectionOrderId, string? InspectionOrderNo,
    decimal Quantity,
    int StorageLocationId, string StorageLocationCode,
    DateOnly CollectedOn, DateOnly? RetainUntil,
    SampleStorageStatus Status,
    DateOnly? ClosedOn, string? Note,
    /// <summary>業務日付時点で保管期限を過ぎているか（＝処分してよい）</summary>
    bool IsRetentionOver,
    /// <summary>期限までの残り日数（期限なしはnull。負数は超過日数）</summary>
    int? DaysUntilRetentionEnd);

// ---- 倉庫業務進捗（D-50-30-07）----

/// <summary>
/// 倉庫業務の進捗（D-50-30-07）。業務の種別ごとに、指示したものがどれだけ片付いたかを見る。
/// 新しいエンティティは持たず、既存の指示（受入・ピッキング・出荷・移動・棚卸）を数え直す
/// </summary>
public record WarehouseProgressRow(
    /// <summary>業務種別（受入・出庫ピッキング・出荷・在庫移動・棚卸）</summary>
    string Kind,
    int TotalCount,
    int CompletedCount,
    int OpenCount,
    /// <summary>未完了のうち最も古いものの経過日数（無ければnull）。滞留を見るための値</summary>
    int? OldestOpenAgeDays);

public record WarehouseProgressResponse(
    DateOnly From, DateOnly To, List<WarehouseProgressRow> Rows);
