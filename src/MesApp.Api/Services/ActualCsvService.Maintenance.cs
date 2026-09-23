using MesApp.Api.Localization;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

// 実績CSVのうち保全（E-30-20 保全指示、E-30-30 突発依頼、E-40-30 保全実績）。
// 指示と実績は MaintenanceOrderService を通す（在庫の引落し・必要スキルの照合・設備の復帰も単票と同じ）
public sealed partial class ActualCsvService
{
    /// <summary>
    /// 保全指示・突発依頼（E-30-20-01 / E-30-30-01）。1行＝1件の指示。
    /// 後続の保全実績CSVが指示を番号で指すため、番号は手入力（MT〜は自動採番用として拒否）
    /// </summary>
    private async Task<int> ImportMaintenanceOrdersAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var equipmentIds = await db.Equipments.AsNoTracking()
            .ToDictionaryAsync(e => e.AssetNo, e => e.Id, StringComparer.Ordinal, ct);
        var toolIds = await db.Tools.AsNoTracking()
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.Ordinal, ct);
        var procedureIds = await db.MaintenanceProcedures.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.ProcedureNo, p => p.Id, StringComparer.Ordinal, ct);
        // 計画保全の指示は保全ロールだけが作れる（突発依頼は全員。単票APIの Create と同じ）
        var canPlan = await IsInGroupAsync(userId, MaintenanceOrderService.PlannedOrderRoles, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var maintenanceNo = reader.RequiredText("MaintenanceNo", 50);
            var equipmentId = reader.Reference("EquipmentAssetNo", null, equipmentIds, "設備");
            var toolId = reader.Reference("ToolCode", null, toolIds, "治工具");
            var fromPlan = reader.Bool("FromPlan", false);
            // 計画から作る指示は計画保全（依頼区分の省略時も計画として扱う）
            var requestType = reader.Enum("RequestType",
                fromPlan ? MaintenanceRequestType.Planned : MaintenanceRequestType.Spot, CsvEnumLabels.MaintenanceRequestTypes);
            var procedureId = reader.Reference("ProcedureNo", null, procedureIds, "有効な保全手順書");
            var scheduledDate = reader.DateOrNull("ScheduledDate", null);
            var note = reader.Text("Note", null, 1000);
            if (requestType == MaintenanceRequestType.Planned && !canPlan && !reader.Failed)
            {
                reader.Fail(ApiText.T("計画保全の指示は保全担当者だけが登録できます（突発依頼は誰でも登録できます）。"));
            }
            if (fromPlan && requestType != MaintenanceRequestType.Planned && !reader.Failed)
            {
                reader.Fail(ApiText.T("保全計画から作る指示の依頼区分は計画にしてください。"));
            }
            var planId = fromPlan && !reader.Failed ? await FindPlanAsync(reader, equipmentId, scheduledDate, ct) : null;
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await maintenanceOrders.CreateAsync(
                new MaintenanceOrderCreateRequest(equipmentId, toolId, planId, procedureId, scheduledDate, requestType, note),
                maintenanceNo, userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>
    /// 保全計画を指す。計画には番号が無いので、同じ設備・同じ予定日の未指示の計画が1件だけのときに限る
    /// （0件・2件以上は行エラー。どの計画かを推測で決めない）
    /// </summary>
    private async Task<int?> FindPlanAsync(
        CsvRowReader reader, int? equipmentId, DateOnly? scheduledDate, CancellationToken ct)
    {
        if (equipmentId is null || scheduledDate is null)
        {
            reader.Fail(ApiText.T("保全計画から作る指示には EquipmentAssetNo（設備）と ScheduledDate（予定日）が必要です。"));
            return null;
        }
        var plans = await db.MaintenancePlans
            .Where(p => p.EquipmentId == equipmentId && p.ScheduledDate == scheduledDate
                        && p.Status == MaintenancePlanStatus.Planned)
            .Select(p => p.Id).ToListAsync(ct);
        if (plans.Count != 1)
        {
            reader.Fail(plans.Count == 0
                ? ApiText.T("設備と予定日 {0} が一致する未指示の保全計画はありません。", scheduledDate)
                : ApiText.T("設備と予定日 {0} が一致する未指示の保全計画が {1} 件あります。予定日を分けてください。", scheduledDate, plans.Count));
            return null;
        }
        return plans[0];
    }

    /// <summary>
    /// 保全計画（E-30-10-01）。1行＝1件の計画で、常に新規登録（自然なキーが無いため上書きしない）。
    /// 計画から保全指示を作るときは、保全指示CSVの FromPlan で設備＋予定日から指す
    /// </summary>
    private async Task<int> ImportMaintenancePlansAsync(
        CsvTable table, List<CsvImportError> errors, string? userId, CancellationToken ct)
    {
        var equipmentIds = await db.Equipments.AsNoTracking().Where(e => e.IsActive)
            .ToDictionaryAsync(e => e.AssetNo, e => e.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            reader.RequiredText("EquipmentAssetNo");
            var equipmentId = reader.Reference("EquipmentAssetNo", null, equipmentIds, "有効な設備");
            var category = reader.Enum("Category", MaintenanceCategory.Periodic, CsvEnumLabels.MaintenanceCategories);
            var scheduledDate = reader.DateOrNull("ScheduledDate", null);
            // 省略時は予定日の年（予定日も無ければ必須）
            var planYear = reader.IntOrNull("PlanYear", null, 2000) ?? scheduledDate?.Year;
            var cycleDays = reader.IntOrNull("CycleDays", null, 1);
            var note = reader.Text("Note", null, 1000);
            if (planYear is null && !reader.Failed)
            {
                reader.Fail(ApiText.T("PlanYear（計画年度）か ScheduledDate（予定日）のどちらかが必要です。"));
            }
            if (reader.Failed)
            {
                continue;
            }

            var outcome = await maintenanceOrders.CreatePlanAsync(
                new MaintenancePlanRequest(equipmentId!.Value, category, planYear!.Value, scheduledDate, cycleDays, note),
                userId, ct);
            if (outcome.Failed)
            {
                FailRow(reader, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>
    /// 保全実績（E-40-30-01）。保全番号が同じ行を1件の実績にまとめ、1行に消費部材を1つ書ける。
    /// 実績の登録で指示は完了するため、1件の指示に実績は1件だけ。実施者は取り込んだユーザー
    /// （手順書の必要スキルもこのユーザーと照合する）
    /// </summary>
    private async Task<int> ImportMaintenanceRecordsAsync(
        CsvTable table, List<CsvImportError> errors, string userId, CancellationToken ct)
    {
        var locationIds = await db.Locations.AsNoTracking()
            .ToDictionaryAsync(l => l.Code, l => l.Id, StringComparer.Ordinal, ct);

        var created = 0;
        foreach (var group in table.Rows.GroupBy(row => table.Value(row, "MaintenanceNo")).Select(g => g.ToList()))
        {
            var head = new CsvRowReader(table, group[0], errors);
            var maintenanceNo = head.RequiredText("MaintenanceNo");
            var startedAt = RequiredDateTime(head, "StartedAt");
            var endedAt = head.DateTimeOrNull("EndedAt", FactoryOffset);
            var result = head.Text("Result", null, 2000);
            var partsUsed = head.Text("PartsUsed", null, 1000);
            var note = head.Text("Note", null, 1000);
            var resetToolLife = head.Bool("ResetToolLife", false);
            // 前のファイル（保全指示CSV）で登録した指示も指せるよう、まとまりごとにDBを引く
            int? orderId = null;
            if (maintenanceNo.Length > 0)
            {
                orderId = await db.MaintenanceOrders.Where(o => o.OrderNo == maintenanceNo)
                    .Select(o => (int?)o.Id).FirstOrDefaultAsync(ct);
                if (orderId is null)
                {
                    head.Fail(ApiText.T("保全指示 '{0}' は登録されていません（MaintenanceNo）。", maintenanceNo));
                }
            }

            var parts = new List<MaintenanceRecordPartRequest>();
            var failed = head.Failed;
            foreach (var row in group)
            {
                var reader = row == group[0] ? head : new CsvRowReader(table, row, errors);
                var part = await ReadMaintenancePartAsync(reader, locationIds, ct);
                if (part is not null)
                {
                    parts.Add(part);
                }
                failed |= reader.Failed;
            }
            if (failed)
            {
                continue;
            }

            var outcome = await maintenanceOrders.AddRecordAsync(orderId!.Value,
                new MaintenanceRecordRequest(startedAt!.Value, endedAt, partsUsed, result, note, resetToolLife, parts),
                userId, ct);
            if (outcome.Failed)
            {
                FailRow(head, outcome.Error!);
                continue;
            }
            created++;
        }
        return created;
    }

    /// <summary>行の消費部材（PartLotNumber を空欄にした行は部材なし）</summary>
    private async Task<MaintenanceRecordPartRequest?> ReadMaintenancePartAsync(
        CsvRowReader reader, IReadOnlyDictionary<string, int> locationIds, CancellationToken ct)
    {
        var lotNumber = reader.Text("PartLotNumber", null);
        var locationId = reader.Reference("PartLocationCode", null, locationIds, "ロケーション");
        var quantity = reader.NumberOrNull("PartQuantity", null, 0.0001m);
        var note = reader.Text("PartNote", null, 500);
        if (lotNumber is null)
        {
            if ((locationId is not null || quantity is not null) && !reader.Failed)
            {
                reader.Fail(ApiText.T("消費部材を書く行には PartLotNumber（部材のロット番号）が必要です。"));
            }
            return null;
        }
        if ((locationId is null || quantity is null) && !reader.Failed)
        {
            reader.Fail(ApiText.T("消費部材には PartLocationCode（払出元ロケーション）と PartQuantity（数量）が必要です。"));
        }
        // 受入CSVなど前のファイルで作ったロットも指せるよう、DBを引く
        var lotId = await db.Lots.Where(l => l.LotNumber == lotNumber)
            .Select(l => (int?)l.Id).FirstOrDefaultAsync(ct);
        if (lotId is null)
        {
            reader.Fail(ApiText.T("ロット '{0}' は登録されていません（PartLotNumber）。", lotNumber));
        }
        return reader.Failed ? null : new MaintenanceRecordPartRequest(lotId!.Value, locationId!.Value, quantity!.Value, note);
    }

    /// <summary>取り込んだユーザーがロールグループに入っているか（単票APIの <c>User.IsInRole</c> の代わり）</summary>
    private async Task<bool> IsInGroupAsync(string? userId, string roleGroup, CancellationToken ct)
    {
        if (userId is null)
        {
            return false;
        }
        var roles = roleGroup.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return await (from userRole in db.UserRoles
                      join role in db.Roles on userRole.RoleId equals role.Id
                      where userRole.UserId == userId && roles.Contains(role.Name!)
                      select userRole.UserId).AnyAsync(ct);
    }
}
