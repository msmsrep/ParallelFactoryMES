namespace MesApp.Core.Contracts.Integration;

/// <summary>
/// 設備イベントの受信（Spec.md 3.8 連携API。docs-dev/EquipmentEventTasks.md EQE-02。B-40-20、E-20-10-01）。
/// ゲートウェイがためたイベントをまとめて送る。1件ごとに採否を返し、1件の不正で他の件を捨てない
/// </summary>
/// <param name="Events">状態変化の並び（順序は問わない。設備ごとに発生時刻の順に並べ直して判定する）</param>
public record EquipmentEventBatchRequest(List<EquipmentEventRequest>? Events);

/// <summary>
/// 1件の状態変化。項目の不足・不正はこの件だけを拒否するため、必須の項目も null を受け付けて応答で知らせる
/// </summary>
/// <param name="EquipmentCode">設備の資産番号</param>
/// <param name="EventId">ゲートウェイが振る一意のID（設備ごとに一意。最大100文字）。送り直しの重複はこれで弾く</param>
/// <param name="OccurredAt">設備側で状態が変わった時刻（オフセット付きで送る）</param>
/// <param name="State">Running / Stopped / Setup / Failure / Idle / Off（電源断・通信断。開いている区間を閉じるだけ）</param>
/// <param name="AlarmCode">アラーム番号（停止・故障の原因を引くのに使う。最大50文字）</param>
/// <param name="WorkOrderNo">処理中の作業指示の指図番号（<paramref name="OperationSeq"/> と組で指定する）</param>
/// <param name="OperationSeq">処理中の作業指示の工程順序</param>
/// <param name="Note">備考（最大500文字）</param>
public record EquipmentEventRequest(
    string? EquipmentCode,
    string? EventId,
    DateTimeOffset? OccurredAt,
    string? State,
    string? AlarmCode = null,
    string? WorkOrderNo = null,
    int? OperationSeq = null,
    string? Note = null);

/// <summary>受信の結果（件数と、送られた順の1件ごとの結果）</summary>
public record EquipmentEventBatchResponse(
    int Accepted, int Duplicate, int Rejected, List<EquipmentEventResultItem> Results);

/// <summary>1件の結果</summary>
/// <param name="Index">送られた並びでの位置（0始まり）</param>
/// <param name="Result">Accepted（採用）/ Duplicate（同じ eventId を採用済み）/ Rejected（拒否。送り直しても同じ内容なら通らない）</param>
/// <param name="Message">拒否の理由、または採用したうえでの注意（作業指示が見つからない等）</param>
public record EquipmentEventResultItem(
    int Index, string? EquipmentCode, string? EventId, string Result, string? Message);
