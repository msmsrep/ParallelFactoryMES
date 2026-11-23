using MesApp.Core.Constants;
using MesApp.Core.Entities;

namespace MesApp.Core.Contracts.Integration;

/// <summary>
/// Webhook の本文の <c>data</c> の形（Spec.md 3.8 Webhook。docs-dev/WebhookTasks.md WHK-03）。
/// <para>
/// <b>受け側との約束</b>。項目は消さない・名前と型を変えない・null になり得るようにしない・列挙の値を消さない。
/// 足すのはよい（受け側は知らない項目を読み飛ばす）。形は <c>tests/MesApp.Api.Tests/WebhookContract.v1.json</c> に固定してある。
/// 項目名は camelCase、区分・状態は列挙の名前（英語）で送る。コード・番号で指し、内部 ID は送らない
/// （例外は、連携API のフィードの位置と同じ値の <see cref="ProductionResultCorrectedData.ProductionResultId"/>）。
/// 通知は薄く保つ。正しい今の値は連携API のフィードで取りに行ってもらう（通知は「読みに行くきっかけ」）
/// </para>
/// </summary>
public static class WebhookPayloads
{
    /// <summary>種類ごとの <c>data</c> の型。発行（<c>WebhookPublisher</c>）がこれと違う型を渡すと例外にする</summary>
    public static IReadOnlyDictionary<string, Type> DataTypes { get; } = new Dictionary<string, Type>
    {
        [WebhookEventTypes.ProductionResultCreated] = typeof(ProductionResultCreatedData),
        [WebhookEventTypes.ProductionResultCorrected] = typeof(ProductionResultCorrectedData),
        [WebhookEventTypes.WorkOrderStatusChanged] = typeof(WorkOrderStatusChangedData),
        [WebhookEventTypes.ManufacturingOrderStatusChanged] = typeof(ManufacturingOrderStatusChangedData),
        [WebhookEventTypes.LotStatusChanged] = typeof(LotStatusChangedData),
        [WebhookEventTypes.NonconformanceCreated] = typeof(NonconformanceCreatedData),
        [WebhookEventTypes.EquipmentStopped] = typeof(EquipmentStoppedData),
    };
}

/// <summary>
/// 生産実績を記録した（<c>production-result.created</c>。B-40-10）。画面・実績CSV・連携API のどれから記録しても出る。
/// 実績の内部 ID は保存するまで決まらないので含めない（業務と同じトランザクションで積むため）。
/// 実績の全項目は連携API の生産実績の取得（<c>production-results</c>）で読む
/// </summary>
/// <param name="OrderNo">製造指図番号</param>
/// <param name="Sequence">工程順序</param>
/// <param name="OutputLotNumber">産出ロット（最終工程の実績だけ）</param>
public record ProductionResultCreatedData(
    string OrderNo,
    string WorkOrderNo,
    int Sequence,
    string ProductCode,
    string ProcessCode,
    decimal GoodQuantity,
    decimal DefectQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? OutputLotNumber);

/// <summary>生産実績を訂正した（<c>production-result.corrected</c>。B-70-30-01）。訂正の一覧は <c>production-result-corrections</c> で読む</summary>
/// <param name="ProductionResultId">訂正した実績（連携API の生産実績の <c>id</c> と同じ値）</param>
public record ProductionResultCorrectedData(
    long ProductionResultId,
    string OrderNo,
    string WorkOrderNo,
    int Sequence,
    decimal BeforeGoodQuantity,
    decimal BeforeDefectQuantity,
    decimal BeforeScrapQuantity,
    decimal BeforeReworkQuantity,
    decimal AfterGoodQuantity,
    decimal AfterDefectQuantity,
    decimal AfterScrapQuantity,
    decimal AfterReworkQuantity,
    string Reason);

/// <summary>作業指示の状態が変わった（<c>work-order.status-changed</c>）。差立・着手・完了・承認・取消のすべて</summary>
public record WorkOrderStatusChangedData(
    string OrderNo,
    string WorkOrderNo,
    int Sequence,
    string ProductCode,
    string ProcessCode,
    WorkOrderStatus FromStatus,
    WorkOrderStatus ToStatus);

/// <summary>製造指図が完了・取消になった（<c>manufacturing-order.status-changed</c>）。承認・発行などの途中の変化は v1 では送らない</summary>
public record ManufacturingOrderStatusChangedData(
    string OrderNo,
    ManufacturingOrderType OrderType,
    string ProductCode,
    decimal Quantity,
    ManufacturingOrderStatus FromStatus,
    ManufacturingOrderStatus ToStatus);

/// <summary>ロットの在庫ステータスが変わった（<c>lot.status-changed</c>。保留・解除・検査待ち・不良・廃棄予定など）</summary>
/// <param name="Source">変更の契機（手動操作／検査／不適合／受入取消）</param>
/// <param name="Reason">理由（入力されたもの）</param>
public record LotStatusChangedData(
    string LotNumber,
    string ProductCode,
    LotStockStatus FromStatus,
    LotStockStatus ToStatus,
    LotStatusChangeSource Source,
    string? Reason);

/// <summary>不適合を起票した（<c>nonconformance.created</c>。C-30-10）。検査の不合格からの自動起票でも出る</summary>
/// <param name="Source">発生源（工程内・受入・検査など）</param>
public record NonconformanceCreatedData(
    string ReportNo,
    NonconformanceSource Source,
    string? LotNumber,
    string? ProductCode,
    string? WorkOrderNo);

/// <summary>
/// 設備が停止・故障の区間に入った（<c>equipment.stopped</c>。E-20-10-03）。手入力・実績CSV・設備イベントのどれでも出る。
/// 手入力・CSV は終わった区間を後から登録することが多いので、<see cref="EndedAt"/> が入っていることがある
/// </summary>
/// <param name="StopCause">停止原因（設備イベントで対応表から引けなければ null。後から追記される）</param>
/// <param name="AlarmCode">設備イベントのアラーム番号（設備イベントのときだけ）</param>
public record EquipmentStoppedData(
    string EquipmentAssetNo,
    EquipmentLogStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? StopCause,
    string? AlarmCode,
    EquipmentLogSource Source,
    string? WorkOrderNo);
