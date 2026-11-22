namespace MesApp.Core.Contracts.Integration;

/// <summary>
/// 連携API の「続きから読む」一覧（Spec.md 3.8 連携API。docs-dev/IntegrationApiTasks.md INT-03）。
/// 呼び出し側は <see cref="NextAfterId"/> を覚えておき、次は <c>afterId</c> に渡す。
/// <see cref="HasMore"/> が true なら、すぐ続けて読める
/// </summary>
/// <param name="NextAfterId">次に <c>afterId</c> へ渡す値（0件なら渡した <c>afterId</c> のまま）</param>
public record IntegrationFeed<T>(List<T> Items, long NextAfterId, bool HasMore);

/// <summary>
/// 生産実績（B-40-10。完成実績を ERP が取りに来るための形）。
/// 品目・工程・ロットなどはコード・番号で返し、内部ID は続きを読む位置（<paramref name="Id"/>）にしか使わない。
/// 区分・状態は列挙の名前（英語。表示言語によらない）
/// </summary>
/// <param name="Id">続きを読む位置（昇順。訂正の <c>ProductionResultId</c> と同じ値）</param>
/// <param name="OrderType">指図区分（Normal / Rework など）。手直し指図の実績を集計から外すときに使う</param>
/// <param name="WorkOrderStatus">作業指示の今の状態。Canceled でも実績（産出ロット）は現物として残る</param>
/// <param name="EquipmentAssetNo">作業指示に割り当てた設備（差立）</param>
/// <param name="OutputLotNumber">産出ロット（最終工程の実績だけ）</param>
/// <param name="OutputLocationCode">入庫先（最終工程の実績だけ）</param>
/// <param name="PerformedBy">記録した利用者のユーザー名</param>
/// <param name="ApprovedAt">製造完了承認（B-40-10-10）の日時。未承認なら null</param>
/// <param name="RecordedAt">MES に記録した日時</param>
public record ProductionResultResponse(
    long Id,
    string OrderNo,
    string OrderType,
    string WorkOrderNo,
    int Sequence,
    string WorkOrderStatus,
    string ProductCode,
    string ProcessCode,
    string? WorkCenterCode,
    string? EquipmentAssetNo,
    decimal GoodQuantity,
    decimal DefectQuantity,
    decimal ScrapQuantity,
    decimal ReworkQuantity,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? OutputLotNumber,
    string? OutputLocationCode,
    string? ShiftCode,
    string PerformedBy,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset RecordedAt);

/// <summary>
/// 生産実績の訂正（B-70-30-01）。実績の一覧は訂正後の今の値を返すので、すでに読んだ実績が
/// 後から訂正されたことはこちらで知る（実績の続きと訂正の続きの2本を読めば取りこぼさない）
/// </summary>
/// <param name="Id">続きを読む位置（昇順）</param>
/// <param name="ProductionResultId">訂正した実績（<see cref="ProductionResultResponse.Id"/>）</param>
/// <param name="CorrectedBy">訂正した利用者のユーザー名</param>
public record ProductionResultCorrectionResponse(
    long Id,
    long ProductionResultId,
    string OrderNo,
    int Sequence,
    decimal BeforeGoodQuantity,
    decimal BeforeDefectQuantity,
    decimal BeforeScrapQuantity,
    decimal BeforeReworkQuantity,
    decimal AfterGoodQuantity,
    decimal AfterDefectQuantity,
    decimal AfterScrapQuantity,
    decimal AfterReworkQuantity,
    string Reason,
    string? CorrectedBy,
    DateTimeOffset CorrectedAt);
