using System.Globalization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// マスタのCSV一括入出力（Spec.md 3.1 マスタ管理の運用支援）。
/// 出力はUTF-8 BOM付き＋CRLFでExcelにそのまま取り込める形式、取込はコードをキーにしたupsert。
/// </summary>
public sealed partial class MasterCsvService(
    MesAppDbContext db,
    UserManager<AppUser> userManager,
    RefreshTokenService refreshTokenService,
    IAuditLogger auditLogger,
    IBusinessDateService businessDate)
{
    /// <summary>ヘッダーのみのテンプレートCSV</summary>
    public static string Template(CsvKindInfo kind) =>
        CsvFile.Format(MasterCsvKinds.ColumnNames(kind), []);

    /// <summary>指定マスタの全件をCSV文字列にする</summary>
    public async Task<string> ExportAsync(CsvKindInfo kind, bool includeInactive, CancellationToken ct)
    {
        var rows = kind.Kind switch
        {
            MasterCsvKinds.Products => await ExportProductsAsync(includeInactive, ct),
            MasterCsvKinds.Processes => await ExportProcessesAsync(includeInactive, ct),
            MasterCsvKinds.Equipments => await ExportEquipmentsAsync(includeInactive, ct),
            MasterCsvKinds.EquipmentParts => await ExportEquipmentPartsAsync(ct),
            MasterCsvKinds.Tools => await ExportToolsAsync(includeInactive, ct),
            MasterCsvKinds.WorkCenters => await ExportWorkCentersAsync(includeInactive, ct),
            MasterCsvKinds.Locations => await ExportLocationsAsync(includeInactive, ct),
            MasterCsvKinds.InspectionItems => await ExportInspectionItemsAsync(includeInactive, ct),
            MasterCsvKinds.ControlItems => await ExportControlItemsAsync(includeInactive, ct),
            MasterCsvKinds.Checklists => await ExportChecklistsAsync(includeInactive, ct),
            MasterCsvKinds.DefectReasons => await ExportDefectReasonsAsync(includeInactive, ct),
            MasterCsvKinds.Skills => await ExportSkillsAsync(includeInactive, ct),
            MasterCsvKinds.Bom => await ExportBomAsync(ct),
            MasterCsvKinds.Routing => await ExportRoutingAsync(ct),
            MasterCsvKinds.WorkProcedures => await ExportWorkProceduresAsync(includeInactive, ct),
            MasterCsvKinds.Shifts => await ExportShiftsAsync(includeInactive, ct),
            MasterCsvKinds.InspectionDevices => await ExportInspectionDevicesAsync(includeInactive, ct),
            MasterCsvKinds.Users => await ExportUsersAsync(includeInactive, ct),
            MasterCsvKinds.UserSkills => await ExportUserSkillsAsync(ct),
            MasterCsvKinds.ProductionPlans => await ExportProductionPlansAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return CsvFile.Format(MasterCsvKinds.ColumnNames(kind), rows);
    }

    private async Task<List<string?[]>> ExportProductsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.Products.AsNoTracking().Include(p => p.DefaultLocation)
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.Code).ToListAsync(ct);
        return [.. items.Select(p => new string?[]
        {
            p.Code, p.Name, p.Unit, p.Specification, p.Type.ToString(), Num(p.StandardDefectRate),
            p.DefaultLocation?.Code, Bool(p.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportProcessesAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.Processes.AsNoTracking()
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.Code).ToListAsync(ct);
        return [.. items.Select(p => new string?[] { p.Code, p.Name, p.Category.ToString(), Bool(p.IsActive) })];
    }

    private async Task<List<string?[]>> ExportEquipmentsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.Equipments.AsNoTracking().Include(e => e.WorkCenter)
            .Where(e => includeInactive || e.IsActive)
            .OrderBy(e => e.AssetNo).ToListAsync(ct);
        return [.. items.Select(e => new string?[]
        {
            e.AssetNo, e.Name, e.WorkCenter?.Code, e.Site, e.Status.ToString(), e.MaintenanceType.ToString(),
            Num(e.MaintenanceThreshold), e.MaintenanceParts, Bool(e.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportEquipmentPartsAsync(CancellationToken ct)
    {
        var items = await db.EquipmentParts.AsNoTracking()
            .Include(p => p.Equipment).Include(p => p.Product)
            .OrderBy(p => p.Equipment!.AssetNo).ThenBy(p => p.Product!.Code)
            .ToListAsync(ct);
        return [.. items.Select(p => new string?[]
        {
            p.Equipment!.AssetNo, p.Product!.Code, p.Category.ToString(), Num(p.QuantityPer), p.Note,
        })];
    }

    private async Task<List<string?[]>> ExportToolsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.Tools.AsNoTracking()
            .Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.Code).ToListAsync(ct);
        return [.. items.Select(t => new string?[]
        {
            t.Code, t.Name, t.ToolType, Num(t.LifeThresholdCount), Num(t.LifeThresholdHours),
            t.Status.ToString(), Bool(t.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportWorkCentersAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.WorkCenters.AsNoTracking().Include(w => w.Parent)
            .Where(w => includeInactive || w.IsActive)
            .OrderBy(w => w.Code).ToListAsync(ct);
        // 上の段から出力すると、取り込み直したときに上位が先に現れて人が読みやすい。
        // Levelは文字列で保存しているためDB側では段の順に並ばず、取得後に並べ直す
        return [.. items.OrderBy(w => w.Level).ThenBy(w => w.Code, StringComparer.Ordinal)
            .Select(w => new string?[]
        {
            w.Code, w.Name, w.Level.ToString(), w.Parent?.Code, Bool(w.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportLocationsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.Locations.AsNoTracking().Include(l => l.WorkCenter)
            .Where(l => includeInactive || l.IsActive)
            .OrderBy(l => l.Code).ToListAsync(ct);
        return [.. items.Select(l => new string?[]
        {
            l.Code, l.WorkCenter?.Code, l.AreaType.ToString(), l.ShelfNo, Bool(l.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportDefectReasonsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.DefectReasons.AsNoTracking()
            .Where(r => includeInactive || r.IsActive)
            .OrderBy(r => r.Code).ToListAsync(ct);
        return [.. items.Select(r => new string?[]
        {
            r.Code, r.Name, r.Category.ToString(), Bool(r.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportInspectionItemsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.InspectionItems.AsNoTracking()
            .Include(i => i.TargetProduct).Include(i => i.TargetProcess)
            .Where(i => includeInactive || i.IsActive)
            .OrderBy(i => i.Code).ToListAsync(ct);
        return [.. items.Select(i => new string?[]
        {
            i.Code, i.Name, i.TargetProduct?.Code, i.TargetProcess?.Code, i.Type.ToString(),
            Num(i.LowerLimit), Num(i.UpperLimit), Num(i.StandardValue), i.Method, Num(i.SamplingCount),
            Bool(i.IsActive), i.Version.ToString(CultureInfo.InvariantCulture),
        })];
    }

    private async Task<List<string?[]>> ExportControlItemsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.ControlItems.AsNoTracking()
            .Include(i => i.TargetProduct).Include(i => i.TargetProcess)
            .Where(i => includeInactive || i.IsActive)
            .OrderBy(i => i.Code).ToListAsync(ct);
        return [.. items.Select(i => new string?[]
        {
            i.Code, i.Name, i.Unit, i.TargetProduct?.Code, i.TargetProcess?.Code,
            Num(i.TargetValue), Num(i.LowerLimit), Num(i.UpperLimit), Bool(i.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportChecklistsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.Checklists.AsNoTracking().Include(c => c.Items)
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Code).ToListAsync(ct);

        var rows = new List<string?[]>();
        foreach (var checklist in items)
        {
            if (checklist.Items.Count == 0)
            {
                rows.Add([checklist.Code, checklist.Name, checklist.Category.ToString(), Bool(checklist.IsActive),
                    null, null, null]);
                continue;
            }
            foreach (var item in checklist.Items.OrderBy(i => i.Sequence))
            {
                rows.Add([checklist.Code, checklist.Name, checklist.Category.ToString(), Bool(checklist.IsActive),
                    Num(item.Sequence), item.Text, Bool(item.IsRequired)]);
            }
        }
        return rows;
    }

    private async Task<List<string?[]>> ExportSkillsAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.Skills.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .OrderBy(s => s.Code).ToListAsync(ct);
        return [.. items.Select(s => new string?[]
        {
            s.Code, s.Name, s.Type.ToString(), Bool(s.RequiresExpiry), Bool(s.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportBomAsync(CancellationToken ct)
    {
        var items = await db.BomItems.AsNoTracking()
            .Include(b => b.ParentProduct).Include(b => b.ChildProduct)
            .OrderBy(b => b.ParentProduct!.Code).ThenBy(b => b.ChildProduct!.Code)
            .ToListAsync(ct);
        return [.. items.Select(b => new string?[]
        {
            b.ParentProduct!.Code, b.ChildProduct!.Code, Num(b.QuantityPer),
            b.MakeOrBuy.ToString(), b.AlternativeGroup, Bool(b.IsAlternative),
            b.RoutingSequence?.ToString(CultureInfo.InvariantCulture),
        })];
    }

    private async Task<List<string?[]>> ExportRoutingAsync(CancellationToken ct)
    {
        var items = await db.Routings.AsNoTracking()
            .Include(r => r.Product).Include(r => r.Process)
            .Include(r => r.RequiredSkill).Include(r => r.Equipment)
            .Include(r => r.Tool).Include(r => r.Checklist).Include(r => r.WorkCenter)
            .Include(r => r.WorkProcedure)
            .Include(r => r.EquipmentCandidates).ThenInclude(c => c.Equipment)
            .OrderBy(r => r.Product!.Code).ThenBy(r => r.Sequence)
            .ToListAsync(ct);
        return [.. items.Select(r => new string?[]
        {
            r.Product!.Code, Num(r.Sequence), r.Process!.Code,
            Num(r.StandardWorkMinutes), Num(r.StandardSetupMinutes),
            r.RequiredSkill?.Code, r.Equipment?.AssetNo,
            string.Join(";", r.EquipmentCandidates.Select(c => c.Equipment!.AssetNo).Order(StringComparer.Ordinal)),
            r.Tool?.Code, r.WorkCenter?.Code,
            r.Checklist?.Code, r.ControlItems, r.WorkProcedure?.ProcedureNo,
        })];
    }

    private async Task<List<string?[]>> ExportWorkProceduresAsync(bool includeInactive, CancellationToken ct)
    {
        var items = await db.WorkProcedures.AsNoTracking()
            .Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.ProcedureNo).ToListAsync(ct);
        return [.. items.Select(p => new string?[]
        {
            p.ProcedureNo, p.Title, p.Steps, p.Reference, Bool(p.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportUsersAsync(bool includeInactive, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().Include(u => u.WorkCenter).Include(u => u.Shift)
            .Where(u => includeInactive || u.IsActive)
            .OrderBy(u => u.UserName).ToListAsync(ct);
        var roles = await RoleNamesByUserAsync(ct);

        return [.. users.Select(u => new string?[]
        {
            u.UserName, u.DisplayName,
            roles.TryGetValue(u.Id, out var names) ? string.Join(";", names) : null,
            u.WorkCenter?.Code, u.Department, u.Shift?.Code, Bool(u.IsActive), null,
        })];
    }

    private async Task<List<string?[]>> ExportShiftsAsync(bool includeInactive, CancellationToken ct)
    {
        var shifts = await db.Shifts.AsNoTracking()
            .Where(s => includeInactive || s.IsActive)
            .ToListAsync(ct);
        // 直は時間帯で並べる（一覧APIと同じ並び。TimeOnlyはSQLiteで並べ替えられないため取り出してから）
        return [.. shifts
            .OrderBy(s => s.StartTime).ThenBy(s => s.Code, StringComparer.Ordinal)
            .Select(s => new string?[]
            {
                s.Code, s.Name, s.StartTime.ToString("HH:mm"), s.EndTime.ToString("HH:mm"), Bool(s.IsActive),
            })];
    }

    private async Task<List<string?[]>> ExportInspectionDevicesAsync(bool includeInactive, CancellationToken ct)
    {
        var devices = await db.InspectionDevices.AsNoTracking()
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.Code)
            .ToListAsync(ct);
        return [.. devices.Select(d => new string?[]
        {
            d.Code, d.Name, d.SerialNo, d.Location, Date(d.CalibratedOn), Date(d.CalibrationDueOn),
            d.CalibrationCycleDays?.ToString(CultureInfo.InvariantCulture), d.Note, Bool(d.IsActive),
        })];
    }

    private async Task<List<string?[]>> ExportUserSkillsAsync(CancellationToken ct)
    {
        var items = await db.UserSkills.AsNoTracking()
            .Include(s => s.User).Include(s => s.Skill)
            .OrderBy(s => s.User!.UserName).ThenBy(s => s.Skill!.Code)
            .ToListAsync(ct);
        return [.. items.Select(s => new string?[]
        {
            s.User!.UserName, s.Skill!.Code, Date(s.AcquiredOn), Date(s.ExpiresOn),
        })];
    }

    /// <summary>生産計画は全件を出す（有効・無効の区別は無い）。並びは一覧APIと同じ</summary>
    private async Task<List<string?[]>> ExportProductionPlansAsync(CancellationToken ct)
    {
        var plans = await db.ProductionPlans.AsNoTracking()
            .Include(p => p.Product).Include(p => p.Process).Include(p => p.WorkCenter)
            .ToListAsync(ct);
        return ProductionPlanRows(plans
            .OrderBy(p => p.BusinessDate)
            .ThenBy(p => p.Product!.Code, StringComparer.Ordinal)
            .ThenBy(p => p.Process!.Code, StringComparer.Ordinal)
            .ThenBy(p => p.WorkCenter?.Code, StringComparer.Ordinal));
    }

    /// <summary>
    /// 絞り込んだ計画をCSVにする（生産計画画面の出力 <c>api/production-plans/csv</c>。Spec.md 3.8）。
    /// 品目・工程・作業区を Include 済みで、並べ替え済みの計画を渡す
    /// </summary>
    public static string FormatProductionPlans(IEnumerable<ProductionPlan> plans) =>
        CsvFile.Format(MasterCsvKinds.ColumnNames(MasterCsvKinds.Find(MasterCsvKinds.ProductionPlans)!),
            ProductionPlanRows(plans));

    private static List<string?[]> ProductionPlanRows(IEnumerable<ProductionPlan> plans) =>
        [.. plans.Select(p => new string?[]
        {
            Date(p.BusinessDate), p.Product!.Code, p.Process!.Code, p.WorkCenter?.Code,
            Num(p.PlannedQuantity), p.Note,
        })];

    private async Task<Dictionary<string, List<string>>> RoleNamesByUserAsync(CancellationToken ct)
    {
        var pairs = await (from userRole in db.UserRoles
                           join role in db.Roles on userRole.RoleId equals role.Id
                           select new { userRole.UserId, role.Name }).AsNoTracking().ToListAsync(ct);
        return pairs
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Name ?? string.Empty).Order().ToList());
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string? Num(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Num(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
