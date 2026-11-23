using System.Globalization;
using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MesApp.Api.Services;

/// <summary>
/// 設備イベントの設定（appsettings の <c>EquipmentEvents</c> 節。Spec.md 3.8 連携API）。起動時に読む
/// </summary>
public sealed class EquipmentEventOptions
{
    public const string SectionName = "EquipmentEvents";

    /// <summary>1回の要求で受け付ける件数の上限</summary>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>サーバーの時刻より先の発生時刻を許す幅（秒。設備の時計のずれの許容）</summary>
    public int MaxFutureSkewSeconds { get; set; } = 300;
}

/// <summary>
/// 設備イベントを受けて、設備稼働履歴（<see cref="EquipmentLog"/>）の区間に組み立てる
/// （Spec.md 3.5・3.8。docs-dev/EquipmentEventTasks.md EQE-02。B-40-20-01〜02、E-20-10-01）。
/// <para>
/// 設備ごとに発生時刻の順に並べ、新しい状態のイベントで開いている区間を閉じて次の区間を開く。
/// <b>最後に採用したイベントより古いもの（遅着）は拒否する</b>——間に差し込むと過去の区間が割れ、
/// OEE やロットの履歴がさかのぼって変わるため。
/// </para>
/// <para>
/// 手入力・CSV の登録（<see cref="ShopFloorReportService.AddEquipmentLogAsync"/>）は通さない。
/// あちらは収集する設備を拒否し、停止原因を必須にするが、PLC は原因の文章を持たないため
/// （アラーム番号から対応表で引き、引けなければ後から入れる）。
/// </para>
/// </summary>
public sealed class EquipmentEventService(
    MesAppDbContext db, IAuditLogger auditLogger, IOptions<EquipmentEventOptions> options)
{
    /// <summary>区間を閉じるだけの状態（電源断・通信断）。稼働履歴のステータスには無い</summary>
    public const string OffState = "Off";

    private const int EventIdMaxLength = 100;
    private const int AlarmCodeMaxLength = 50;
    private const int WorkOrderNoMaxLength = 50;
    private const int NoteMaxLength = 500;

    private static readonly Dictionary<string, EquipmentLogStatus> States = new(StringComparer.Ordinal)
    {
        [nameof(EquipmentLogStatus.Running)] = EquipmentLogStatus.Running,
        [nameof(EquipmentLogStatus.Stopped)] = EquipmentLogStatus.Stopped,
        [nameof(EquipmentLogStatus.Setup)] = EquipmentLogStatus.Setup,
        [nameof(EquipmentLogStatus.Failure)] = EquipmentLogStatus.Failure,
        [nameof(EquipmentLogStatus.Idle)] = EquipmentLogStatus.Idle,
    };

    public int MaxBatchSize => options.Value.MaxBatchSize;

    public async Task<EquipmentEventBatchResponse> ReceiveAsync(
        IReadOnlyList<EquipmentEventRequest> events, string? userId, CancellationToken ct)
    {
        var results = new EquipmentEventResultItem?[events.Count];
        var now = DateTimeOffset.UtcNow;
        var maxOccurredAt = now.AddSeconds(Math.Max(0, options.Value.MaxFutureSkewSeconds));

        var codes = events.Select(e => e.EquipmentCode?.Trim()).OfType<string>().Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal).ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var equipments = await db.Equipments.AsNoTracking()
            .Where(e => codes.Contains(e.AssetNo))
            .ToDictionaryAsync(e => e.AssetNo, StringComparer.Ordinal, ct);

        // 同じ設備への同時の要求を直列にする（設備の行を書き換えて行ロックを取る。開いている区間を読む前に）。
        // 並べる順を揃えて、2つの要求が逆の順にロックを取り合わないようにする
        var targetIds = equipments.Values.Where(e => e.IsActive && e.CollectsEvents).Select(e => e.Id).Order().ToList();
        if (targetIds.Count > 0)
        {
            await db.Equipments.Where(e => targetIds.Contains(e.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CollectsEvents, e => e.CollectsEvents), ct);
        }

        // 設備が引けない件は、イベントの控えも残せない（設備IDが無い）ので応答だけで知らせる
        var byEquipment = new Dictionary<int, List<(int Index, EquipmentEventRequest Event)>>();
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var code = e.EquipmentCode?.Trim();
            var eventId = e.EventId?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                results[i] = Rejected(i, e, ApiText.T("equipmentCode（設備の資産番号）がありません。"));
                continue;
            }
            if (!equipments.TryGetValue(code, out var equipment))
            {
                results[i] = Rejected(i, e, ApiText.T("資産番号 '{0}' の設備が見つかりません。", code));
                continue;
            }
            if (string.IsNullOrEmpty(eventId) || eventId.Length > EventIdMaxLength)
            {
                results[i] = Rejected(i, e, ApiText.T("eventId は1〜{0}文字で指定してください。", EventIdMaxLength));
                continue;
            }
            if (!equipment.IsActive)
            {
                results[i] = Rejected(i, e, ApiText.T("設備 '{0}' は無効です。", code));
                continue;
            }
            if (!equipment.CollectsEvents)
            {
                results[i] = Rejected(i, e, ApiText.T("設備 '{0}' は設備イベントで収集する設定になっていません。", code));
                continue;
            }
            if (!byEquipment.TryGetValue(equipment.Id, out var list))
            {
                byEquipment[equipment.Id] = list = [];
            }
            list.Add((i, e));
        }

        var stored = new List<EquipmentEvent>();
        foreach (var (equipmentId, items) in byEquipment)
        {
            await ApplyAsync(equipmentId, items, results, stored, maxOccurredAt, now, userId, ct);
        }

        var accepted = results.Count(r => r?.Result == nameof(EquipmentEventResult.Accepted));
        var duplicate = results.Count(r => r?.Result == nameof(EquipmentEventResult.Duplicate));
        var rejected = results.Length - accepted - duplicate;
        if (stored.Count > 0)
        {
            // 1イベント1行の監査ログは多すぎるので、要求ごとに件数の要約だけを残す（中身は EquipmentEvent にある）
            await auditLogger.LogAsync("Equipment", "ReceiveEvents", nameof(EquipmentEvent), null,
                detail: new { accepted, duplicate, rejected, equipments = byEquipment.Count }, ct: ct);
        }
        await transaction.CommitAsync(ct);

        return new EquipmentEventBatchResponse(accepted, duplicate, rejected, [.. results.Select(r => r!)]);
    }

    /// <summary>1台の設備の件を、発生時刻の順に区間へ反映する</summary>
    private async Task ApplyAsync(
        int equipmentId, List<(int Index, EquipmentEventRequest Event)> items,
        EquipmentEventResultItem?[] results, List<EquipmentEvent> stored,
        DateTimeOffset maxOccurredAt, DateTimeOffset now, string? userId, CancellationToken ct)
    {
        var eventIds = items.Select(x => x.Event.EventId!.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var existing = await db.EquipmentEvents
            .Where(e => e.EquipmentId == equipmentId && eventIds.Contains(e.EventId))
            .ToDictionaryAsync(e => e.EventId, StringComparer.Ordinal, ct);

        // 採用は発生時刻の順に行うので、ID の最も大きい採用済みのイベントが最後に採用したもの
        // （SQLite は DateTimeOffset の比較・並べ替えを SQL にできないため、時刻ではなく ID で引く）
        var lastAccepted = await db.EquipmentEvents.AsNoTracking()
            .Where(e => e.EquipmentId == equipmentId && e.Result == EquipmentEventResult.Accepted)
            .OrderByDescending(e => e.Id).Select(e => (DateTimeOffset?)e.OccurredAt).FirstOrDefaultAsync(ct);
        var openLog = await db.EquipmentLogs
            .Where(l => l.EquipmentId == equipmentId && l.EndedAt == null)
            .OrderByDescending(l => l.Id).FirstOrDefaultAsync(ct);

        // 発生時刻の順（同じ時刻は送られた順）。時刻の無い件は先頭で拒否する
        var ordered = items
            .OrderBy(x => x.Event.OccurredAt ?? DateTimeOffset.MinValue)
            .ThenBy(x => x.Index)
            .ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (index, e) in ordered)
        {
            var eventId = e.EventId!.Trim();
            existing.TryGetValue(eventId, out var previous);
            if (!seen.Add(eventId) || previous?.Result == EquipmentEventResult.Accepted)
            {
                results[index] = new(index, e.EquipmentCode, e.EventId, nameof(EquipmentEventResult.Duplicate), null);
                continue;
            }
            // 前に拒否した同じ eventId は、送り直しとして判定し直す（控えは新しい行に置き換える。
            // ID の順が採用の順になるよう、古い行を残して書き換えることはしない）
            if (previous is not null)
            {
                db.EquipmentEvents.Remove(previous);
                await db.SaveChangesAsync(ct);
                existing.Remove(eventId);
            }

            var row = new EquipmentEvent
            {
                EquipmentId = equipmentId,
                EventId = eventId,
                OccurredAt = e.OccurredAt ?? now,
                State = Truncate(e.State?.Trim() ?? string.Empty, 30)!,
                AlarmCode = Truncate(e.AlarmCode?.Trim(), AlarmCodeMaxLength),
                WorkOrderNo = Truncate(e.WorkOrderNo?.Trim(), WorkOrderNoMaxLength),
                OperationSeq = e.OperationSeq,
                Note = Truncate(e.Note?.Trim(), NoteMaxLength),
                ReceivedAt = now,
                ReceivedByUserId = userId,
            };

            var rejection = Validate(e, maxOccurredAt, lastAccepted ?? openLog?.StartedAt, openLog?.StartedAt);
            if (rejection is { } reason)
            {
                row.Result = EquipmentEventResult.Rejected;
                // 保存する文字列なので訳さない（応答は表示言語で返す）
                row.RejectReason = Truncate(reason.Original, 500);
                db.EquipmentEvents.Add(row);
                stored.Add(row);
                results[index] = new(index, e.EquipmentCode, e.EventId, nameof(EquipmentEventResult.Rejected), reason.Localized);
                continue;
            }

            var occurredAt = e.OccurredAt!.Value;
            var isOff = e.State!.Trim() == OffState;
            var status = isOff ? (EquipmentLogStatus?)null : States[e.State.Trim()];
            string? message = null;

            if (openLog is not null && status == openLog.Status)
            {
                // 同じ状態が続けて来た：区間はそのまま（控えは残す）
                row.EquipmentLogId = openLog.Id;
            }
            else if (openLog is { Source: EquipmentLogSource.Event } && openLog.StartedAt == occurredAt)
            {
                // 開いた区間と同じ時刻の変化：長さ0の区間は作らず、開いた区間を置き換える
                // （手入力で開いた区間は消さない。普通に閉じる）
                if (status is null)
                {
                    await DetachEventsAsync(openLog.Id, stored, ct);
                    db.EquipmentLogs.Remove(openLog);
                    await db.SaveChangesAsync(ct);
                    openLog = null;
                }
                else
                {
                    var (workOrderId, note) = await ResolveWorkOrderAsync(equipmentId, e, ct);
                    message = note;
                    openLog.Status = status.Value;
                    openLog.StopCause = await StopCauseAsync(equipmentId, status.Value, row.AlarmCode, ct);
                    openLog.WorkOrderId = workOrderId;
                    openLog.Note = row.Note;
                    row.EquipmentLogId = openLog.Id;
                }
            }
            else
            {
                if (openLog is not null)
                {
                    openLog.EndedAt = occurredAt;
                    row.EquipmentLogId = openLog.Id;
                }
                if (status is { } next)
                {
                    var (workOrderId, note) = await ResolveWorkOrderAsync(equipmentId, e, ct);
                    message = note;
                    openLog = new EquipmentLog
                    {
                        EquipmentId = equipmentId,
                        WorkOrderId = workOrderId,
                        Status = next,
                        StartedAt = occurredAt,
                        // 停止原因は PLC から来ないので、アラーム番号から対応表で引く。引けなければ空で開き、画面から後で入れる
                        StopCause = await StopCauseAsync(equipmentId, next, row.AlarmCode, ct),
                        Note = row.Note,
                        RecordedByUserId = userId,
                        Source = EquipmentLogSource.Event,
                    };
                    db.EquipmentLogs.Add(openLog);
                    await db.SaveChangesAsync(ct);
                    row.EquipmentLogId = openLog.Id;
                }
                else
                {
                    openLog = null;
                }
            }

            row.Result = EquipmentEventResult.Accepted;
            db.EquipmentEvents.Add(row);
            // 次の件の遅着判定と、ID の順＝採用の順を保つため、1件ずつ保存する
            await db.SaveChangesAsync(ct);
            stored.Add(row);
            lastAccepted = occurredAt;
            results[index] = new(index, e.EquipmentCode, e.EventId, nameof(EquipmentEventResult.Accepted), message);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>件の中身の検査。拒否するときは保存用（原文）と応答用（表示言語）の理由を返す</summary>
    private static (string Original, string Localized)? Validate(
        EquipmentEventRequest e, DateTimeOffset maxOccurredAt, DateTimeOffset? lastAccepted, DateTimeOffset? openStartedAt)
    {
        if (e.OccurredAt is not { } occurredAt)
        {
            return Reason("occurredAt（発生時刻）がありません。");
        }
        var state = e.State?.Trim();
        if (string.IsNullOrEmpty(state) || state != OffState && !States.ContainsKey(state))
        {
            return Reason("state '{0}' は使えません（Running / Stopped / Setup / Failure / Idle / Off）。", state ?? "");
        }
        if (e.AlarmCode?.Trim().Length > AlarmCodeMaxLength)
        {
            return Reason("{0} は{1}文字以内にしてください。", "alarmCode", AlarmCodeMaxLength);
        }
        if (e.WorkOrderNo?.Trim().Length > WorkOrderNoMaxLength)
        {
            return Reason("{0} は{1}文字以内にしてください。", "workOrderNo", WorkOrderNoMaxLength);
        }
        if (e.Note?.Trim().Length > NoteMaxLength)
        {
            return Reason("{0} は{1}文字以内にしてください。", "note", NoteMaxLength);
        }
        if (string.IsNullOrWhiteSpace(e.WorkOrderNo) != (e.OperationSeq is null))
        {
            return Reason("workOrderNo（指図番号）と operationSeq（工程順序）は組で指定してください。");
        }
        if (occurredAt > maxOccurredAt)
        {
            return Reason("発生時刻 {0} がサーバーの時刻より先です。設備・ゲートウェイの時計を合わせてください。",
                occurredAt.ToString("o", CultureInfo.InvariantCulture));
        }
        // 最後に採用したイベント（手入力の開いた区間しか無ければその開始）より前は遅着
        var last = lastAccepted is { } a && openStartedAt is { } o ? (a > o ? a : o) : lastAccepted ?? openStartedAt;
        if (last is { } boundary && occurredAt < boundary)
        {
            return Reason("最後に反映した時刻 {0} より前のイベントです（遅着）。過去の区間は組み直しません。",
                boundary.ToString("o", CultureInfo.InvariantCulture));
        }
        return null;
    }

    private static (string Original, string Localized) Reason(string text, params object[] args) =>
        (string.Format(CultureInfo.InvariantCulture, text, args), ApiText.T(text, args));

    /// <summary>
    /// 区間の作業指示。指定があればそれを（見つからなければ紐付けずに注意を返す）、無ければ
    /// <b>その設備を割り当てた着手中の作業指示がちょうど1件</b>のときだけ紐付ける
    /// （改訂65の「差立の割当からは補完しない」と違い、着手は作業者が現に始めたという根拠がある。複数なら推測しない）
    /// </summary>
    private async Task<(int? WorkOrderId, string? Message)> ResolveWorkOrderAsync(
        int equipmentId, EquipmentEventRequest e, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(e.WorkOrderNo) && e.OperationSeq is { } seq)
        {
            var orderNo = e.WorkOrderNo.Trim();
            var id = await db.WorkOrders.AsNoTracking()
                .Where(w => w.ManufacturingOrder!.OrderNo == orderNo && w.RoutingSequence == seq)
                .Select(w => (int?)w.Id).FirstOrDefaultAsync(ct);
            return id is null
                ? (null, ApiText.T("作業指示（指図番号 '{0}'、工程順序 {1}）が見つからないため、作業指示に紐付けずに記録しました。", orderNo, seq))
                : (id, null);
        }
        var started = await db.WorkOrders.AsNoTracking()
            .Where(w => w.AssignedEquipmentId == equipmentId && w.Status == WorkOrderStatus.Started)
            .Select(w => w.Id).Take(2).ToListAsync(ct);
        return (started.Count == 1 ? started[0] : null, null);
    }

    /// <summary>停止・故障の区間だけ、アラーム番号から停止原因を引く（Spec.md 5.1 EquipmentAlarmCode）</summary>
    private async Task<string?> StopCauseAsync(
        int equipmentId, EquipmentLogStatus status, string? alarmCode, CancellationToken ct) =>
        status is EquipmentLogStatus.Stopped or EquipmentLogStatus.Failure
            ? await EquipmentAlarmCodePolicy.ResolveStopCauseAsync(db, equipmentId, alarmCode, ct)
            : null;

    /// <summary>消す区間を指していた控えの参照を外す（控えは残す）</summary>
    private async Task DetachEventsAsync(int logId, List<EquipmentEvent> stored, CancellationToken ct)
    {
        foreach (var row in stored.Where(r => r.EquipmentLogId == logId))
        {
            row.EquipmentLogId = null;
        }
        await db.SaveChangesAsync(ct);
        await db.EquipmentEvents.Where(r => r.EquipmentLogId == logId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.EquipmentLogId, (int?)null), ct);
    }

    private static EquipmentEventResultItem Rejected(int index, EquipmentEventRequest e, string message) =>
        new(index, e.EquipmentCode, e.EventId, nameof(EquipmentEventResult.Rejected), message);

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
