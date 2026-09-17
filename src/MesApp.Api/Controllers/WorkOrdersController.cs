using System.Security.Claims;
using MesApp.Api.Policies;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Production;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 作業指示（Spec.md 5.2 WorkOrder）と差立（B-10-20：作業員割当（スキル照合 F-20-30-01）・設備割当・着手順）
/// </summary>
[ApiController]
[Route("api/work-orders")]
[Authorize]
public class WorkOrdersController(
    MesAppDbContext db,
    WorkOrderStatusService workOrderStatus,
    IBusinessDateService businessDate,
    IAuditLogger auditLogger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<WorkOrderResponse>>> List(
        [FromQuery] PageQuery paging,
        [FromQuery] int? manufacturingOrderId = null,
        [FromQuery] WorkOrderStatus? status = null,
        [FromQuery] int? processId = null,
        [FromQuery] string? assignedUserId = null,
        CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (manufacturingOrderId is not null)
        {
            query = query.Where(w => w.ManufacturingOrderId == manufacturingOrderId);
        }
        if (status is not null)
        {
            query = query.Where(w => w.Status == status);
        }
        if (processId is not null)
        {
            query = query.Where(w => w.ProcessId == processId);
        }
        if (assignedUserId is not null)
        {
            query = query.Where(w => w.AssignedUserId == assignedUserId);
        }

        // 差立で設定した着手順を優先して並べる（B-10-20-03）
        var workOrders = await query
            .OrderBy(w => w.DispatchOrder == null).ThenBy(w => w.DispatchOrder)
            .ThenBy(w => w.ManufacturingOrderId).ThenBy(w => w.RoutingSequence)
            .ToPagedResultAsync(paging, ct);
        return workOrders.Map(w => ToResponse(w, w.ManufacturingOrder!));
    }

    /// <summary>
    /// 作業指示選択用の選択肢（トラブル報告・検査指示・治工具利用実績などの対象指定）。
    /// 指示番号・品目コード/名称・工程コード/名称の部分一致で絞り込む。
    /// </summary>
    [HttpGet("options")]
    public async Task<ActionResult<OptionsResult<WorkOrderResponse>>> Options(
        [FromQuery] OptionQuery options, CancellationToken ct = default)
    {
        var query = BaseQuery();
        if (options.Keyword is { } keyword)
        {
            query = query.Where(w =>
                w.WorkOrderNo.Contains(keyword)
                || w.Product!.Code.Contains(keyword)
                || w.Product!.Name.Contains(keyword)
                || w.Process!.Code.Contains(keyword)
                || w.Process!.Name.Contains(keyword));
        }
        var result = await query
            .OrderBy(w => w.DispatchOrder == null).ThenBy(w => w.DispatchOrder)
            .ThenBy(w => w.ManufacturingOrderId).ThenBy(w => w.RoutingSequence)
            .ToOptionsResultAsync(options, ct);
        return new OptionsResult<WorkOrderResponse>(
            [.. result.Items.Select(w => ToResponse(w, w.ManufacturingOrder!))], result.Truncated);
    }

    /// <summary>
    /// 工程別の進捗集計（B-60-10-01）。作業指示を全件取ってから画面で数えると
    /// 件数が増えるほど重くなるため、集計はDB側で行う。取消は対象外。
    /// <para>
    /// <paramref name="workCenterId"/> を指定すると、その資源と**配下すべて**の作業区に
    /// 展開して絞り込む。作業指示が持つ作業区は最下段（工順のスナップショット）なので、
    /// 展開せずに絞るとラインや工場を選んだときに常に0件になる。
    /// </para>
    /// </summary>
    [HttpGet("process-summary")]
    public async Task<ActionResult<List<ProcessProgressRow>>> ProcessSummary(
        [FromQuery] int? workCenterId = null, CancellationToken ct = default)
    {
        var query = db.WorkOrders.AsNoTracking()
            .Where(w => w.Status != WorkOrderStatus.Canceled);
        if (workCenterId is { } rootId)
        {
            var all = await db.WorkCenters.AsNoTracking().ToListAsync(ct);
            if (all.All(x => x.Id != rootId))
            {
                return this.BadRequestProblem($"作業区（ID {rootId}）が見つかりません。");
            }
            var targets = WorkCenterHierarchyPolicy.SelfAndDescendantIds(rootId, all);
            query = query.Where(w => w.WorkCenterId != null && targets.Contains(w.WorkCenterId.Value));
        }
        return await query
            .GroupBy(w => new { w.ProcessId, w.Process!.Code, w.Process!.Name })
            .OrderBy(g => g.Key.Code)
            .Select(g => new ProcessProgressRow(
                g.Key.ProcessId, g.Key.Code, g.Key.Name,
                g.Count(w => w.Status == WorkOrderStatus.Created),
                g.Count(w => w.Status == WorkOrderStatus.Dispatched),
                g.Count(w => w.Status == WorkOrderStatus.Started),
                g.Count(w => w.Status == WorkOrderStatus.Completed),
                g.Count(w => w.Status == WorkOrderStatus.Approved)))
            .ToListAsync(ct);
    }

    /// <summary>
    /// 遅れている作業指示（A-30-20-01）。次の2つを返す。
    /// <list type="bullet">
    /// <item>指図の納期を過ぎているのに完了していない作業指示</item>
    /// <item>着手済みで、経過時間が予定時間を <paramref name="overrunPercent"/> 以上超えている作業指示</item>
    /// </list>
    /// <para>
    /// 通知の仕組み（メール等）は持たないため、画面に出すところまでを担う。
    /// 予定時間が0分（標準時間が未設定）の工程は超過を判定できないため対象にしない。
    /// </para>
    /// </summary>
    [HttpGet("delays")]
    public async Task<ActionResult<List<WorkOrderDelayRow>>> Delays(
        [FromQuery] decimal overrunPercent = 20m, CancellationToken ct = default)
    {
        var today = businessDate.Today;
        var open = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Status != WorkOrderStatus.Canceled
                        && w.Status != WorkOrderStatus.Completed
                        && w.Status != WorkOrderStatus.Approved)
            .Select(w => new
            {
                w.Id,
                w.WorkOrderNo,
                OrderNo = w.ManufacturingOrder!.OrderNo,
                DueDate = w.ManufacturingOrder!.DueDate,
                ProductCode = w.Product!.Code,
                ProcessCode = w.Process!.Code,
                w.Status,
                w.PlannedQuantity,
                w.StandardSetupMinutes,
                w.StandardWorkMinutes,
            })
            .ToListAsync(ct);
        if (open.Count == 0)
        {
            return new List<WorkOrderDelayRow>();
        }

        // 着手時刻は作業指示に持たせていないので、状態履歴の最初の「着手」から取る（Spec.md 5.2）
        var ids = open.Select(w => w.Id).ToList();
        var startedAt = (await db.WorkOrderStatusHistories.AsNoTracking()
                .Where(h => ids.Contains(h.WorkOrderId) && h.ToStatus == WorkOrderStatus.Started)
                .Select(h => new { h.WorkOrderId, h.ChangedAt })
                .ToListAsync(ct))
            .GroupBy(h => h.WorkOrderId)
            .ToDictionary(g => g.Key, g => g.Min(h => h.ChangedAt));

        var now = DateTimeOffset.Now;
        var rows = new List<WorkOrderDelayRow>();
        foreach (var w in open)
        {
            var planned = w.StandardSetupMinutes + w.StandardWorkMinutes * w.PlannedQuantity;
            var started = startedAt.GetValueOrDefault(w.Id);
            var elapsed = started == default
                ? 0m
                : Math.Round((decimal)(now - started).TotalMinutes, 2);

            if (w.DueDate is { } due && due < today)
            {
                rows.Add(new WorkOrderDelayRow(
                    w.Id, w.WorkOrderNo, w.OrderNo, w.ProductCode, w.ProcessCode, w.Status,
                    WorkOrderDelayKind.OverdueDueDate, due, today.DayNumber - due.DayNumber,
                    started == default ? null : started, planned, elapsed, null));
                continue;
            }

            // 標準時間が未設定の工程は超過を判定できない（0分に対する超過率は意味を持たない）
            if (started == default || planned <= 0)
            {
                continue;
            }
            var overrun = Math.Round((elapsed - planned) / planned * 100, 2);
            if (overrun >= overrunPercent)
            {
                rows.Add(new WorkOrderDelayRow(
                    w.Id, w.WorkOrderNo, w.OrderNo, w.ProductCode, w.ProcessCode, w.Status,
                    WorkOrderDelayKind.OverrunStandardTime, w.DueDate, null,
                    started, planned, elapsed, overrun));
            }
        }

        return rows
            .OrderByDescending(r => r.OverdueDays ?? 0)
            .ThenByDescending(r => r.OverrunPercent ?? 0)
            .ThenBy(r => r.WorkOrderNo, StringComparer.Ordinal)
            .ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WorkOrderResponse>> Get(int id, CancellationToken ct)
    {
        var workOrder = await BaseQuery().FirstOrDefaultAsync(w => w.Id == id, ct);
        return workOrder is null ? NotFound() : ToResponse(workOrder, workOrder.ManufacturingOrder!);
    }

    /// <summary>
    /// 差立で選べる候補設備（B-10-20-02）。工順に候補が無ければ空を返す
    /// （その場合は設備を限定しないため、画面は設備マスタ全件から選ばせる）
    /// </summary>
    [HttpGet("{id:int}/equipment-candidates")]
    public async Task<ActionResult<List<WorkOrderEquipmentCandidate>>> EquipmentCandidates(
        int id, CancellationToken ct)
    {
        var workOrder = await db.WorkOrders.AsNoTracking()
            .Select(w => new { w.Id, w.ProductId, w.ProcessId, w.RoutingSequence })
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        return await db.RoutingEquipments.AsNoTracking()
            .Where(c => c.Routing!.ProductId == workOrder.ProductId
                        && c.Routing!.ProcessId == workOrder.ProcessId
                        && c.Routing!.Sequence == workOrder.RoutingSequence)
            .OrderBy(c => c.Equipment!.AssetNo)
            .Select(c => new WorkOrderEquipmentCandidate(
                c.EquipmentId, c.Equipment!.AssetNo, c.Equipment!.Name, c.Equipment!.IsActive))
            .ToListAsync(ct);
    }

    /// <summary>
    /// 工程管理項目の指示（B-30-30-04）。    /// <summary>
    /// 工程管理項目の指示（B-30-30-04）。展開時点のマスタを写したもので、
    /// 実績の逸脱判定と画面表示はこれを使う（マスタの現在値を参照しない。Spec.md 5.7）
    /// </summary>
    [HttpGet("{id:int}/control-items")]
    public async Task<ActionResult<List<WorkOrderControlItemResponse>>> ControlItems(
        int id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            return NotFound();
        }
        return await db.WorkOrderControlItems.AsNoTracking()
            .Where(i => i.WorkOrderId == id)
            .OrderBy(i => i.ItemCode)
            .Select(i => new WorkOrderControlItemResponse(
                i.Id, i.ControlItemId, i.ItemCode, i.ItemName, i.Unit,
                i.ItemVersion, i.TargetValue, i.LowerLimit, i.UpperLimit))
            .ToListAsync(ct);
    }

    /// <summary>
    /// 作業手順書（SOP。B-10-30-03「作業指示書に書かれている作業手順などの内容を確認する」）。
    /// 手順の本文はマスタの現在値を返し、展開時点の版数と突き合わせて改訂の有無を示す。
    /// 工順に手順書が紐付いていない作業指示は 404。
    /// </summary>
    [HttpGet("{id:int}/procedure")]
    public async Task<ActionResult<WorkOrderProcedureResponse>> Procedure(int id, CancellationToken ct)
    {
        var workOrder = await db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new { w.Id, w.WorkProcedureId, w.WorkProcedureVersion })
            .FirstOrDefaultAsync(ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.WorkProcedureId is not int procedureId)
        {
            return this.NotFoundProblem("この作業指示には作業手順書が紐付いていません。");
        }
        var procedure = await db.WorkProcedures.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == procedureId, ct);
        if (procedure is null)
        {
            return NotFound();
        }
        return new WorkOrderProcedureResponse(
            procedure.Id, procedure.ProcedureNo, procedure.Title, procedure.Steps, procedure.Reference,
            procedure.Version, workOrder.WorkProcedureVersion,
            workOrder.WorkProcedureVersion is { } planned && planned != procedure.Version,
            procedure.IsActive);
    }

    /// <summary>
    /// 状態履歴（Spec.md 5.2 WorkOrderStatusHistory）。配布・着手・完了・承認・取消の遷移を時系列で返す
    /// </summary>
    [HttpGet("{id:int}/status-history")]
    public async Task<ActionResult<List<WorkOrderStatusHistoryEntry>>> StatusHistory(
        int id, CancellationToken ct)
    {
        if (!await db.WorkOrders.AnyAsync(w => w.Id == id, ct))
        {
            return NotFound();
        }
        return await db.WorkOrderStatusHistories.AsNoTracking()
            .Where(h => h.WorkOrderId == id)
            .OrderBy(h => h.Id)
            .Select(h => new WorkOrderStatusHistoryEntry(
                h.FromStatus, h.ToStatus, h.Source, h.Note, h.ChangedBy!.DisplayName, h.ChangedAt))
            .ToListAsync(ct);
    }

    /// <summary>
    /// 差立（B-10-20-01〜03）。作業員割当時は工順の必要スキルと照合し、
    /// スキル未保有・期限切れなら割当を拒否する（F-20-30-01）。着手済み以降は変更不可。
    /// </summary>
    [HttpPut("{id:int}/dispatch")]
    [Authorize(Roles = MesRoleGroups.ProductionManage)]
    public async Task<ActionResult<WorkOrderResponse>> Dispatch(int id, DispatchRequest request, CancellationToken ct)
    {
        var workOrder = await BaseQuery(track: true).FirstOrDefaultAsync(w => w.Id == id, ct);
        if (workOrder is null)
        {
            return NotFound();
        }
        if (workOrder.Status is not (WorkOrderStatus.Created or WorkOrderStatus.Dispatched))
        {
            return this.ConflictProblem($"状態 '{workOrder.Status}' の作業指示は差立できません。");
        }

        // 作業員割当：スキル・資格照合（F-20-30-01）
        if (request.AssignedUserId is not null)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == request.AssignedUserId, ct);
            if (user is null || !user.IsActive)
            {
                return this.BadRequestProblem("割当作業者が存在しないか無効です。");
            }

            // 必要スキルは工順マスタの現在値ではなく、展開時点のスナップショットを使う（Spec.md 5.7）
            if (workOrder.RequiredSkillId is int skillId)
            {
                var today = businessDate.Today;
                var userSkill = await db.UserSkills.Include(s => s.Skill)
                    .FirstOrDefaultAsync(s => s.UserId == user.Id && s.SkillId == skillId, ct);
                var skillName = userSkill?.Skill?.Name
                    ?? await db.Skills.Where(s => s.Id == skillId).Select(s => s.Name).FirstAsync(ct);
                if (userSkill is null)
                {
                    return this.BadRequestProblem(
                        $"作業者 '{user.DisplayName}' は必要スキル '{skillName}' を保有していません。");
                }
                if (userSkill.Skill!.RequiresExpiry && (userSkill.ExpiresOn is null || userSkill.ExpiresOn < today))
                {
                    return this.BadRequestProblem(
                        $"作業者 '{user.DisplayName}' のスキル '{skillName}' は有効期限切れです。");
                }
            }
        }

        // 設備割当（B-10-20-02）
        if (request.AssignedEquipmentId is int equipmentId)
        {
            var equipment = await db.Equipments.FindAsync([equipmentId], ct);
            if (equipment is null || !equipment.IsActive)
            {
                return this.BadRequestProblem("割当設備が存在しないか無効です。");
            }
            // 停止中・保全中・廃棄の設備には新しく割り当てさせない。既に割り当て済みの設備のまま
            // 着手順だけを変える差立は通す（保全に入った設備の作業指示を画面から触れなくしないため）
            if (equipmentId != workOrder.AssignedEquipmentId && equipment.Status != EquipmentStatus.Available)
            {
                return this.ConflictProblem(
                    $"設備 '{equipment.AssetNo}' は{EquipmentStatusLabel(equipment.Status)}のため割り当てられません。");
            }

            // 工順に候補設備が登録されていれば、その中からしか選べない。
            // 候補は工順マスタの現在値を見る（設備は差立で決めるためスナップショットに含めない：Spec.md 5.7）。
            // 候補が未登録の工順は従来どおり設備を限定しない
            var candidates = await db.RoutingEquipments.AsNoTracking()
                .Where(c => c.Routing!.ProductId == workOrder.ProductId
                            && c.Routing!.ProcessId == workOrder.ProcessId
                            && c.Routing!.Sequence == workOrder.RoutingSequence)
                .Select(c => new { c.EquipmentId, c.Equipment!.AssetNo })
                .ToListAsync(ct);
            if (candidates.Count > 0 && candidates.All(c => c.EquipmentId != equipmentId))
            {
                return this.BadRequestProblem(
                    $"設備 '{equipment.AssetNo}' はこの工程の候補設備ではありません" +
                    $"（候補：{string.Join("、", candidates.Select(c => c.AssetNo))}）。");
            }
        }

        workOrder.AssignedUserId = request.AssignedUserId;
        workOrder.AssignedEquipmentId = request.AssignedEquipmentId;
        workOrder.DispatchOrder = request.DispatchOrder;
        workOrderStatus.ChangeStatus(workOrder, WorkOrderStatus.Dispatched,
            WorkOrderStatusChangeSource.Dispatch, User.FindFirstValue(ClaimTypes.NameIdentifier));
        await db.SaveChangesAsync(ct);
        await auditLogger.LogAsync("Production", "Dispatch", nameof(WorkOrder), id.ToString(),
            detail: $"workOrderNo={workOrder.WorkOrderNo}, user={request.AssignedUserId}, " +
                    $"equipment={request.AssignedEquipmentId}, order={request.DispatchOrder}", ct: ct);

        var updated = await BaseQuery().FirstAsync(w => w.Id == id, ct);
        return ToResponse(updated, updated.ManufacturingOrder!);
    }

    private IQueryable<WorkOrder> BaseQuery(bool track = false)
    {
        var query = db.WorkOrders
            .Include(w => w.ManufacturingOrder)
            .Include(w => w.Product)
            .Include(w => w.Process)
            .Include(w => w.AssignedUser)
            .Include(w => w.AssignedEquipment)
            .AsQueryable();
        return track ? query : query.AsNoTracking();
    }

    private static string EquipmentStatusLabel(EquipmentStatus status) => status switch
    {
        EquipmentStatus.Available => "稼働可能",
        EquipmentStatus.Stopped => "停止中",
        EquipmentStatus.UnderMaintenance => "保全中",
        EquipmentStatus.Retired => "廃棄・除却",
        _ => status.ToString(),
    };

    internal static WorkOrderResponse ToResponse(WorkOrder w, ManufacturingOrder order) =>
        new(w.Id, w.WorkOrderNo, w.ManufacturingOrderId, order.OrderNo,
            w.ProductId, w.Product?.Code ?? order.Product!.Code, w.Product?.Name ?? order.Product!.Name,
            w.ProcessId, w.Process!.Code, w.Process!.Name,
            w.RoutingSequence, w.PlannedQuantity, w.DispatchOrder,
            w.AssignedUserId, w.AssignedUser?.DisplayName,
            w.AssignedEquipmentId, w.AssignedEquipment?.Name,
            w.Status,
            w.StandardWorkMinutes, w.StandardSetupMinutes, w.ControlItems);
}
