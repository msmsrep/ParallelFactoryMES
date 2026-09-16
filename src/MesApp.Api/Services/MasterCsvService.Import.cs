using MesApp.Api.Policies;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// マスタCSVの取込。コード（自然キー）で既存を検索し、あれば更新・無ければ新規登録する。
/// 1行でもエラーがあれば全件ロールバックし、行番号付きのエラー一覧を返す（部分反映はしない）。
/// </summary>
public sealed partial class MasterCsvService
{
    /// <summary>取込可能な最大データ行数</summary>
    public const int MaxRows = 20000;

    /// <summary>応答に含めるエラーの最大件数</summary>
    private const int MaxReportedErrors = 200;

    private sealed class ImportCounter
    {
        public int Created { get; set; }
        public int Updated { get; set; }
    }

    public async Task<CsvImportResult> ImportAsync(
        CsvKindInfo kind, string csvText, bool dryRun, CancellationToken ct)
    {
        var errors = new List<CsvImportError>();
        var table = CsvTable.Create(CsvFile.Parse(csvText));
        if (table is null)
        {
            errors.Add(new CsvImportError(1, "CSVが空です。1行目にヘッダー行が必要です。"));
            return Result(kind, 0, new ImportCounter(), dryRun, errors);
        }

        var missing = kind.Columns.Where(c => c.Required && !table.HasColumn(c.Name)).Select(c => c.Name).ToList();
        if (missing.Count > 0)
        {
            errors.Add(new CsvImportError(table.Header.Line,
                $"必須の列がありません：{string.Join(", ", missing)}。テンプレートCSVの1行目をそのまま使ってください。"));
            return Result(kind, 0, new ImportCounter(), dryRun, errors);
        }
        if (table.Rows.Count == 0)
        {
            errors.Add(new CsvImportError(table.Header.Line, "データ行がありません。"));
            return Result(kind, 0, new ImportCounter(), dryRun, errors);
        }
        if (table.Rows.Count > MaxRows)
        {
            errors.Add(new CsvImportError(table.Header.Line,
                $"1回に取り込めるのは{MaxRows}行までです（{table.Rows.Count}行）。ファイルを分割してください。"));
            return Result(kind, 0, new ImportCounter(), dryRun, errors);
        }

        var counter = new ImportCounter();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            switch (kind.Kind)
            {
                case MasterCsvKinds.Products:
                    await ImportProductsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Processes:
                    await ImportProcessesAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Equipments:
                    await ImportEquipmentsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.EquipmentParts:
                    await ImportEquipmentPartsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Tools:
                    await ImportToolsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.WorkCenters:
                    await ImportWorkCentersAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Locations:
                    await ImportLocationsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.InspectionItems:
                    await ImportInspectionItemsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.ControlItems:
                    await ImportControlItemsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Checklists:
                    await ImportChecklistsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.DefectReasons:
                    await ImportDefectReasonsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Skills:
                    await ImportSkillsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Bom:
                    await ImportBomAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Routing:
                    await ImportRoutingAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.WorkProcedures:
                    await ImportWorkProceduresAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.InspectionDevices:
                    await ImportInspectionDevicesAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Shifts:
                    await ImportShiftsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Users:
                    await ImportUsersAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.UserSkills:
                    await ImportUserSkillsAsync(table, errors, counter, ct);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (errors.Count == 0 && !dryRun)
            {
                await db.SaveChangesAsync(ct);
                await auditLogger.LogAsync("Master", "CsvImport", kind.Kind, null,
                    detail: $"rows={table.Rows.Count}, created={counter.Created}, updated={counter.Updated}", ct: ct);
                await transaction.CommitAsync(ct);
            }
            else
            {
                await transaction.RollbackAsync(ct);
            }
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(ct);
            errors.Add(new CsvImportError(0, $"DBへの反映に失敗しました：{ex.InnerException?.Message ?? ex.Message}"));
        }

        return Result(kind, table.Rows.Count, counter, dryRun, errors);
    }

    private static CsvImportResult Result(
        CsvKindInfo kind, int dataRows, ImportCounter counter, bool dryRun, List<CsvImportError> errors)
    {
        var succeeded = errors.Count == 0;
        if (errors.Count > MaxReportedErrors)
        {
            var omitted = errors.Count - MaxReportedErrors;
            errors = [.. errors.Take(MaxReportedErrors), new CsvImportError(0, $"他 {omitted} 件のエラーは省略しました。")];
        }
        return new CsvImportResult(
            kind.Kind, dataRows,
            succeeded ? counter.Created : 0,
            succeeded ? counter.Updated : 0,
            dryRun, succeeded, errors);
    }

    // ---- 単票マスタ ----

    private async Task ImportProductsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.Products.ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "品目コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var product);
            product ??= new Product { Code = code };

            var name = reader.RequiredText("Name", 200);
            var unit = reader.RequiredText("Unit", 20);
            var specification = reader.Text("Specification", product.Specification);
            var type = reader.Enum("Type", product.Type, CsvEnumLabels.ProductTypes);
            var defectRate = reader.Number("StandardDefectRate", product.StandardDefectRate, 0, 100);
            var isActive = reader.Bool("IsActive", product.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            product.Name = name;
            product.Unit = unit;
            product.Specification = specification;
            product.Type = type;
            product.StandardDefectRate = defectRate;
            product.IsActive = isActive;
            product.UpdatedAt = DateTimeOffset.UtcNow;

            if (isNew)
            {
                db.Products.Add(product);
                byCode[code] = product;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportProcessesAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.Processes.ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "工程コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var process);
            process ??= new ProcessMaster { Code = code };

            var name = reader.RequiredText("Name", 200);
            var category = reader.Enum("Category", process.Category, CsvEnumLabels.MakeOrBuys);
            var isActive = reader.Bool("IsActive", process.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            process.Name = name;
            process.Category = category;
            process.IsActive = isActive;

            if (isNew)
            {
                db.Processes.Add(process);
                byCode[code] = process;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportEquipmentsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byAssetNo = await db.Equipments.ToDictionaryAsync(e => e.AssetNo, StringComparer.Ordinal, ct);
        var workCenters = await db.WorkCenters.AsNoTracking()
            .ToDictionaryAsync(w => w.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var assetNo = reader.RequiredText("AssetNo", 50);
            if (reader.Failed || !CheckUnique(reader, seen, assetNo, "資産番号"))
            {
                continue;
            }

            var isNew = !byAssetNo.TryGetValue(assetNo, out var equipment);
            equipment ??= new Equipment { AssetNo = assetNo };

            var name = reader.RequiredText("Name", 200);
            var site = reader.Text("Site", equipment.Site, 200);
            var workCenter = ResolveWorkCenter(
                reader, table, "WorkCenterCode", equipment.WorkCenterId, workCenters, out var workCenterKept);
            var status = reader.Enum("Status", equipment.Status, CsvEnumLabels.EquipmentStatuses);
            var maintenanceType = reader.Enum("MaintenanceType", equipment.MaintenanceType, CsvEnumLabels.MaintenanceTypes);
            var threshold = reader.NumberOrNull("MaintenanceThreshold", equipment.MaintenanceThreshold, 0);
            var parts = reader.Text("MaintenanceParts", equipment.MaintenanceParts);
            var isActive = reader.Bool("IsActive", equipment.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            // 設備は作業区（最下段）にだけ紐付ける。判定は単票APIと同じ（Spec.md 5.1 Equipment）
            if (!workCenterKept && WorkCenterHierarchyPolicy.CheckEquipmentPlacement(workCenter) is { } placement)
            {
                reader.Fail(placement);
                continue;
            }

            equipment.Name = name;
            equipment.Site = site;
            if (!workCenterKept)
            {
                equipment.WorkCenterId = workCenter?.Id;
            }
            equipment.Status = status;
            equipment.MaintenanceType = maintenanceType;
            equipment.MaintenanceThreshold = threshold;
            equipment.MaintenanceParts = parts;
            equipment.IsActive = isActive;

            if (isNew)
            {
                db.Equipments.Add(equipment);
                byAssetNo[assetNo] = equipment;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    /// <summary>
    /// 設備の保全部品。単票APIと同じく設備ごとの一括置換にする
    /// （行単位の追加だと、CSVから削除したつもりの部品が残る）
    /// </summary>
    private async Task ImportEquipmentPartsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var equipmentIds = await db.Equipments.AsNoTracking()
            .ToDictionaryAsync(e => e.AssetNo, e => e.Id, StringComparer.Ordinal, ct);
        var productIds = await db.Products.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);

        foreach (var group in GroupRows(table, "EquipmentAssetNo", errors))
        {
            var groupReader = new CsvRowReader(table, group.First(), errors);
            var assetNo = groupReader.RequiredText("EquipmentAssetNo");
            if (!equipmentIds.TryGetValue(assetNo, out var equipmentId))
            {
                groupReader.Fail($"設備 '{assetNo}' は登録されていません。先に設備マスタを取り込んでください。");
                continue;
            }

            var parts = new List<EquipmentPart>();
            var seenProducts = new HashSet<int>();
            var failed = false;
            foreach (var row in group)
            {
                var reader = new CsvRowReader(table, row, errors);
                var productId = reader.Reference("ProductCode", null, productIds, "部品の品目");
                var category = reader.Enum("Category", MaintenancePartCategory.Consumable,
                    CsvEnumLabels.MaintenancePartCategories);
                var quantity = reader.Number("QuantityPer", 0m, 0);
                var note = reader.Text("Note", null, 500);
                if (productId is null && !reader.Failed)
                {
                    reader.Fail("ProductCode（部品の品目コード）は必須です。");
                }
                if (productId is { } id && !seenProducts.Add(id))
                {
                    reader.Fail($"設備 '{assetNo}' に同じ品目が複数行あります。");
                }
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                parts.Add(new EquipmentPart
                {
                    EquipmentId = equipmentId,
                    ProductId = productId!.Value,
                    Category = category,
                    QuantityPer = quantity,
                    Note = note,
                });
            }
            if (failed)
            {
                continue;
            }

            var existing = await db.EquipmentParts.Where(p => p.EquipmentId == equipmentId).ToListAsync(ct);
            db.EquipmentParts.RemoveRange(existing);
            db.EquipmentParts.AddRange(parts);
            if (existing.Count == 0)
            {
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportToolsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.Tools.ToDictionaryAsync(t => t.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "治工具コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var tool);
            tool ??= new Tool { Code = code };

            var name = reader.RequiredText("Name", 200);
            var toolType = reader.Text("ToolType", tool.ToolType, 100);
            var lifeCount = reader.IntOrNull("LifeThresholdCount", tool.LifeThresholdCount, 0);
            var lifeHours = reader.NumberOrNull("LifeThresholdHours", tool.LifeThresholdHours, 0);
            var status = reader.Enum("Status", tool.Status, CsvEnumLabels.ToolStatuses);
            var isActive = reader.Bool("IsActive", tool.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            tool.Name = name;
            tool.ToolType = toolType;
            tool.LifeThresholdCount = lifeCount;
            tool.LifeThresholdHours = lifeHours;
            tool.Status = status;
            tool.IsActive = isActive;

            if (isNew)
            {
                db.Tools.Add(tool);
                byCode[code] = tool;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportDefectReasonsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.DefectReasons.ToDictionaryAsync(r => r.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "不良理由コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var reason);
            reason ??= new DefectReason { Code = code };

            var name = reader.RequiredText("Name", 200);
            var category = reader.Enum("Category", reason.Category, CsvEnumLabels.DefectReasonCategories);
            var isActive = reader.Bool("IsActive", reason.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            reason.Name = name;
            reason.Category = category;
            reason.IsActive = isActive;

            if (isNew)
            {
                db.DefectReasons.Add(reason);
                byCode[code] = reason;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    /// <summary>
    /// 作業区の取込。上位をコードで参照するため2周する。
    /// 1周目で全行の実体を用意し（同じファイル内で上位が後に書かれていても引けるようにする）、
    /// 2周目で上位を結び付けて階層の妥当性を <see cref="WorkCenterHierarchyPolicy"/> で検証する。
    /// 判定を単票APIと共有するので、フォームからは作れない階層がCSVからだけ通ることがない。
    /// </summary>
    /// <summary>
    /// CSVの作業区コード列を解決する。列が無いときは現在値を保つ（<paramref name="kept"/> が true）。
    /// 列があって空欄なら「紐付けを外す」意味になるため null を返す。
    /// </summary>
    private static WorkCenter? ResolveWorkCenter(
        CsvRowReader reader, CsvTable table, string column, int? currentId,
        IReadOnlyDictionary<string, WorkCenter> byCode, out bool kept)
    {
        if (!table.HasColumn(column))
        {
            kept = true;
            return currentId is null ? null : byCode.Values.FirstOrDefault(w => w.Id == currentId);
        }
        kept = false;
        var code = reader.Text(column, null, 50);
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }
        if (byCode.TryGetValue(code, out var found))
        {
            return found;
        }
        reader.Fail($"作業区 '{code}' は登録されていません（{column}）。");
        return null;
    }

    private async Task ImportWorkCentersAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.WorkCenters.ToDictionaryAsync(w => w.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var staged = new List<(CsvRowReader Reader, WorkCenter Entity, string? ParentCode, bool IsNew)>();

        // 1周目：実体を用意し、上位以外の項目を埋める
        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "作業区コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var workCenter);
            workCenter ??= new WorkCenter { Code = code };

            var name = reader.Text("Name", workCenter.Name, 200) ?? workCenter.Name;
            var level = reader.Enum("Level", workCenter.Level, CsvEnumLabels.WorkCenterLevels);
            var parentCode = reader.Text("ParentCode", null, 50);
            var isActive = reader.Bool("IsActive", workCenter.IsActive);
            if (reader.Failed)
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                reader.Fail("Name は必須です。");
                continue;
            }

            workCenter.Name = name;
            workCenter.Level = level;
            workCenter.IsActive = isActive;

            if (isNew)
            {
                db.WorkCenters.Add(workCenter);
                byCode[code] = workCenter;
            }
            staged.Add((reader, workCenter, parentCode, isNew));
        }

        // 2周目：上位を結び付けて階層を検証する
        var all = byCode.Values.ToList();
        foreach (var (reader, workCenter, parentCode, isNew) in staged)
        {
            WorkCenter? parent = null;
            if (!string.IsNullOrWhiteSpace(parentCode))
            {
                if (!byCode.TryGetValue(parentCode, out parent))
                {
                    reader.Fail($"上位の作業区 '{parentCode}' は登録されていません（ParentCode）。");
                    continue;
                }
            }
            // 列が無いときは現在の上位を保つ（列単位の部分更新を既存マスタと揃える）
            else if (!table.HasColumn("ParentCode") && workCenter.ParentId is { } currentParentId)
            {
                parent = all.FirstOrDefault(x => x.Id == currentParentId);
            }

            var selfId = isNew ? (int?)null : workCenter.Id;
            if (WorkCenterHierarchyPolicy.Check(workCenter.Code, workCenter.Level, parent, selfId, all) is { } reason)
            {
                reader.Fail(reason);
                continue;
            }

            // 新規の上位はまだIdを持たないため、ナビゲーションで結ぶ（保存時にEFがIdを埋める）
            workCenter.Parent = parent;
            workCenter.ParentId = parent?.Id;

            if (isNew)
            {
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportLocationsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.Locations.ToDictionaryAsync(l => l.Code, StringComparer.Ordinal, ct);
        var workCenters = await db.WorkCenters.AsNoTracking()
            .ToDictionaryAsync(w => w.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "ロケーションコード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var location);
            location ??= new Location { Code = code };

            var areaType = reader.Enum("AreaType", location.AreaType, CsvEnumLabels.LocationAreaTypes);
            var workCenter = ResolveWorkCenter(
                reader, table, "WorkCenterCode", location.WorkCenterId, workCenters, out var workCenterKept);
            var shelfNo = reader.Text("ShelfNo", location.ShelfNo, 50);
            var isActive = reader.Bool("IsActive", location.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            // 倉庫は工場直下に置かれることがあるため段は問わない（Spec.md 5.1 Location）
            if (!workCenterKept && WorkCenterHierarchyPolicy.CheckLocationPlacement(workCenter) is { } placement)
            {
                reader.Fail(placement);
                continue;
            }

            location.AreaType = areaType;
            location.ShelfNo = shelfNo;
            if (!workCenterKept)
            {
                location.WorkCenterId = workCenter?.Id;
            }
            location.IsActive = isActive;

            if (isNew)
            {
                db.Locations.Add(location);
                byCode[code] = location;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportInspectionItemsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.InspectionItems.ToDictionaryAsync(i => i.Code, StringComparer.Ordinal, ct);
        var productIds = await ProductIdsAsync(ct);
        var processIds = await db.Processes.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "検査項目コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var item);
            item ??= new InspectionItem { Code = code };

            var name = reader.RequiredText("Name", 200);
            var productId = reader.Reference("TargetProductCode", item.TargetProductId, productIds, "対象品目");
            var processId = reader.Reference("TargetProcessCode", item.TargetProcessId, processIds, "対象工程");
            var type = reader.Enum("Type", item.Type, CsvEnumLabels.InspectionTypes);
            var lower = reader.NumberOrNull("LowerLimit", item.LowerLimit);
            var upper = reader.NumberOrNull("UpperLimit", item.UpperLimit);
            var standard = reader.NumberOrNull("StandardValue", item.StandardValue);
            var method = reader.Text("Method", item.Method, 200);
            var sampling = reader.IntOrNull("SamplingCount", item.SamplingCount, 0);
            var isActive = reader.Bool("IsActive", item.IsActive);
            if (lower is not null && upper is not null && lower > upper)
            {
                reader.Fail("規格値の下限が上限を超えています。");
            }
            if (reader.Failed)
            {
                continue;
            }

            // 基準そのものが変わる更新は版数を上げる（C-10-10-03）
            var criteriaChanged = !isNew &&
                (item.Type != type || item.LowerLimit != lower || item.UpperLimit != upper
                 || item.StandardValue != standard || item.Method != method || item.SamplingCount != sampling);

            item.Name = name;
            item.TargetProductId = productId;
            item.TargetProcessId = processId;
            item.Type = type;
            item.LowerLimit = lower;
            item.UpperLimit = upper;
            item.StandardValue = standard;
            item.Method = method;
            item.SamplingCount = sampling;
            item.IsActive = isActive;
            if (criteriaChanged)
            {
                item.Version++;
            }

            if (isNew)
            {
                db.InspectionItems.Add(item);
                byCode[code] = item;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportSkillsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.Skills.ToDictionaryAsync(s => s.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "スキル・資格コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var skill);
            skill ??= new SkillMaster { Code = code };

            var name = reader.RequiredText("Name", 200);
            var type = reader.Enum("Type", skill.Type, CsvEnumLabels.SkillTypes);
            var requiresExpiry = reader.Bool("RequiresExpiry", skill.RequiresExpiry);
            var isActive = reader.Bool("IsActive", skill.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            skill.Name = name;
            skill.Type = type;
            skill.RequiresExpiry = requiresExpiry;
            skill.IsActive = isActive;

            if (isNew)
            {
                db.Skills.Add(skill);
                byCode[code] = skill;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    // ---- 明細を持つマスタ（同一キーの行をまとめて一括置換）----

    private async Task ImportControlItemsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.ControlItems.ToDictionaryAsync(i => i.Code, StringComparer.Ordinal, ct);
        var productIds = await db.Products.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);
        var processIds = await db.Processes.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 50);
            if (reader.Failed || !CheckUnique(reader, seen, code, "工程管理項目コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var item);
            item ??= new ControlItem { Code = code };

            var name = reader.RequiredText("Name", 200);
            var unit = reader.Text("Unit", item.Unit, 30);
            var productId = reader.Reference("TargetProductCode", item.TargetProductId, productIds, "対象品目");
            var processId = reader.Reference("TargetProcessCode", item.TargetProcessId, processIds, "対象工程");
            var target = reader.NumberOrNull("TargetValue", item.TargetValue);
            var lower = reader.NumberOrNull("LowerLimit", item.LowerLimit);
            var upper = reader.NumberOrNull("UpperLimit", item.UpperLimit);
            var isActive = reader.Bool("IsActive", item.IsActive);
            if (reader.Failed)
            {
                continue;
            }
            // 判定条件は単票APIと同じにする（片方だけ通る状態を作らない。Spec.md 7.4）
            if (lower is { } l && upper is { } u && l > u)
            {
                reader.Fail("許容下限は許容上限以下で指定してください。");
                continue;
            }
            if (target is { } tv && ((lower is { } lo && tv < lo) || (upper is { } up && tv > up)))
            {
                reader.Fail("指示値が許容範囲の外にあります。");
                continue;
            }

            item.Name = name;
            item.Unit = unit;
            item.TargetProductId = productId;
            item.TargetProcessId = processId;
            item.TargetValue = target;
            item.LowerLimit = lower;
            item.UpperLimit = upper;
            item.IsActive = isActive;

            if (isNew)
            {
                db.ControlItems.Add(item);
                byCode[code] = item;
                counter.Created++;
            }
            else
            {
                item.Version++; // 条件の改訂
                counter.Updated++;
            }
        }
    }

    private async Task ImportChecklistsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.Checklists.Include(c => c.Items)
            .ToDictionaryAsync(c => c.Code, StringComparer.Ordinal, ct);

        foreach (var group in GroupRows(table, "Code", errors))
        {
            var headerReader = new CsvRowReader(table, group.First(), errors);
            var code = headerReader.RequiredText("Code", 50);
            var isNew = !byCode.TryGetValue(code, out var checklist);
            checklist ??= new Checklist { Code = code };

            var name = headerReader.RequiredText("Name", 200);
            var category = headerReader.Enum("Category", checklist.Category, CsvEnumLabels.ChecklistCategories);
            var isActive = headerReader.Bool("IsActive", checklist.IsActive);

            var items = new List<ChecklistItem>();
            var sequences = new HashSet<int>();
            var failed = headerReader.Failed;
            foreach (var row in group)
            {
                var reader = new CsvRowReader(table, row, errors);
                var sequenceText = table.Value(row, "Sequence");
                var text = table.Value(row, "Text");
                if (sequenceText is null && text is null)
                {
                    continue; // 項目なしの見出し行
                }

                var sequence = reader.IntOrNull("Sequence", null, 1);
                var itemText = reader.RequiredText("Text", 500);
                var isRequired = reader.Bool("IsRequired", true);
                if (sequence is null)
                {
                    reader.Fail("Sequence（項目の表示順）は1以上の整数で指定してください。");
                }
                else if (!sequences.Add(sequence.Value))
                {
                    reader.Fail($"チェックリスト '{code}' の項目の表示順 {sequence} が重複しています。");
                }
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                items.Add(new ChecklistItem { Sequence = sequence!.Value, Text = itemText, IsRequired = isRequired });
            }
            if (failed)
            {
                continue;
            }

            checklist.Name = name;
            checklist.Category = category;
            checklist.IsActive = isActive;
            checklist.Items.Clear();
            checklist.Items.AddRange(items);

            if (isNew)
            {
                db.Checklists.Add(checklist);
                byCode[code] = checklist;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportBomAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var productIds = await ProductIdsAsync(ct);
        var existing = await db.BomItems.ToListAsync(ct);

        foreach (var group in GroupRows(table, "ParentProductCode", errors))
        {
            var parentReader = new CsvRowReader(table, group.First(), errors);
            var parentCode = parentReader.RequiredText("ParentProductCode");
            if (!productIds.TryGetValue(parentCode, out var parentId))
            {
                parentReader.Fail($"親品目 '{parentCode}' は登録されていません。先に品目マスタを取り込んでください。");
                continue;
            }

            var lines = new List<BomItem>();
            var children = new HashSet<int>();
            var failed = false;
            foreach (var row in group)
            {
                var reader = new CsvRowReader(table, row, errors);
                var childCode = reader.RequiredText("ChildProductCode");
                var childId = reader.Reference("ChildProductCode", null, productIds, "子品目");
                var quantity = reader.NumberOrNull("QuantityPer", null, 0.000001m);
                var makeOrBuy = reader.Enum("MakeOrBuy", MakeOrBuy.InHouse, CsvEnumLabels.MakeOrBuys);
                var alternativeGroup = reader.Text("AlternativeGroup", null, 50);
                var isAlternative = reader.Bool("IsAlternative", false);
                if (quantity is null && !reader.Failed)
                {
                    reader.Fail("QuantityPer（必要数量）は必須です。");
                }
                if (childId == parentId)
                {
                    reader.Fail($"品目 '{childCode}' 自身をMBOMの子品目にはできません。");
                }
                else if (childId is not null && !children.Add(childId.Value))
                {
                    reader.Fail($"親品目 '{parentCode}' に子品目 '{childCode}' が重複しています。");
                }
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                lines.Add(new BomItem
                {
                    ParentProductId = parentId,
                    ChildProductId = childId!.Value,
                    QuantityPer = quantity!.Value,
                    MakeOrBuy = makeOrBuy,
                    AlternativeGroup = alternativeGroup,
                    IsAlternative = isAlternative,
                });
            }
            if (failed)
            {
                continue;
            }

            var current = existing.Where(b => b.ParentProductId == parentId).ToList();
            db.BomItems.RemoveRange(current);
            db.BomItems.AddRange(lines);
            if (current.Count > 0)
            {
                counter.Updated++;
            }
            else
            {
                counter.Created++;
            }
        }
    }

    private async Task ImportWorkProceduresAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byNo = await db.WorkProcedures.ToDictionaryAsync(p => p.ProcedureNo, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // 参照中の手順書を無効化できないのは単票APIと同じ。行ごとに引けるよう先にまとめて読む
        // （工順は別種別のCSVなので、この取込の途中で参照関係が変わることはない）
        var referencingProducts = (await db.Routings.AsNoTracking()
                .Where(r => r.WorkProcedureId != null)
                .Select(r => new { ProcedureId = r.WorkProcedureId!.Value, ProductCode = r.Product!.Code })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.ProcedureId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<string>)g.Select(x => x.ProductCode).ToList());

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var procedureNo = reader.RequiredText("ProcedureNo", 50);
            if (reader.Failed || !CheckUnique(reader, seen, procedureNo, "手順書番号"))
            {
                continue;
            }

            var isNew = !byNo.TryGetValue(procedureNo, out var procedure);
            procedure ??= new WorkProcedure { ProcedureNo = procedureNo };

            var title = reader.RequiredText("Title", 200);
            var steps = reader.Text("Steps", procedure.Steps, 4000) ?? string.Empty;
            var reference = reader.Text("Reference", procedure.Reference, 500);
            var isActive = reader.Bool("IsActive", procedure.IsActive);
            if (!reader.Failed && string.IsNullOrWhiteSpace(steps) && string.IsNullOrWhiteSpace(reference))
            {
                reader.Fail("Steps（手順ステップ）かReference（手順書の所在）のどちらかを指定してください。");
            }
            if (!isNew && procedure.IsActive && !isActive
                && MasterDeactivationPolicy.CheckWorkProcedure(
                    procedureNo, referencingProducts.GetValueOrDefault(procedure.Id, [])) is { } inUse)
            {
                reader.Fail(inUse);
            }
            if (reader.Failed)
            {
                continue;
            }

            procedure.Title = title;
            procedure.Steps = steps;
            procedure.Reference = reference;
            procedure.IsActive = isActive;

            if (isNew)
            {
                db.WorkProcedures.Add(procedure);
                byNo[procedureNo] = procedure;
                counter.Created++;
            }
            else
            {
                procedure.Version++; // 取込による改訂も版数を上げる（I-30-40-02）
                counter.Updated++;
            }
        }
    }

    private async Task ImportRoutingAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var productIds = await ProductIdsAsync(ct);
        var processIds = await db.Processes.AsNoTracking()
            .ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);
        var skillIds = await db.Skills.AsNoTracking()
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.Ordinal, ct);
        var equipmentIds = await db.Equipments.AsNoTracking()
            .ToDictionaryAsync(e => e.AssetNo, e => e.Id, StringComparer.Ordinal, ct);
        var toolIds = await db.Tools.AsNoTracking()
            .ToDictionaryAsync(t => t.Code, t => t.Id, StringComparer.Ordinal, ct);
        var checklistIds = await db.Checklists.AsNoTracking()
            .ToDictionaryAsync(c => c.Code, c => c.Id, StringComparer.Ordinal, ct);
        // 無効な手順書は候補に入れない（単票APIと同じ条件）
        var workProcedureIds = await db.WorkProcedures.AsNoTracking().Where(p => p.IsActive)
            .ToDictionaryAsync(p => p.ProcedureNo, p => p.Id, StringComparer.Ordinal, ct);
        // 工順の作業区は最下段のみ（単票APIと同じ条件。Spec.md 5.7）。
        // 候補をここで絞ることで、上位の段を書いた行は「登録されていません」として弾かれる
        var workCenterIds = await db.WorkCenters.AsNoTracking()
            .Where(w => w.Level == WorkCenterLevel.WorkCenter && w.IsActive)
            .ToDictionaryAsync(w => w.Code, w => w.Id, StringComparer.Ordinal, ct);
        var existing = await db.Routings.ToListAsync(ct);

        foreach (var group in GroupRows(table, "ProductCode", errors))
        {
            var productReader = new CsvRowReader(table, group.First(), errors);
            var productCode = productReader.RequiredText("ProductCode");
            if (!productIds.TryGetValue(productCode, out var productId))
            {
                productReader.Fail($"品目 '{productCode}' は登録されていません。先に品目マスタを取り込んでください。");
                continue;
            }

            var steps = new List<Routing>();
            var sequences = new HashSet<int>();
            var failed = false;
            foreach (var row in group)
            {
                var reader = new CsvRowReader(table, row, errors);
                var sequence = reader.IntOrNull("Sequence", null, 1);
                var processId = reader.Reference("ProcessCode", null, processIds, "工程");
                var work = reader.Number("StandardWorkMinutes", 0m, 0);
                var setup = reader.Number("StandardSetupMinutes", 0m, 0);
                var skillId = reader.Reference("RequiredSkillCode", null, skillIds, "スキル・資格");
                var equipmentId = reader.Reference("EquipmentAssetNo", null, equipmentIds, "設備");
                var candidateIds = ParseCandidates(reader, table, row, equipmentIds);
                var toolId = reader.Reference("ToolCode", null, toolIds, "治工具");
                var checklistId = reader.Reference("ChecklistCode", null, checklistIds, "チェックリスト");
                var workCenterId = reader.Reference("WorkCenterCode", null, workCenterIds, "作業区");
                var controlItems = reader.Text("ControlItems", null, 500);
                var workProcedureId = reader.Reference("WorkProcedureNo", null, workProcedureIds, "作業手順書");
                if (sequence is null && !reader.Failed)
                {
                    reader.Fail("Sequence（工程順序）は1以上の整数で指定してください。");
                }
                if (processId is null && !reader.Failed)
                {
                    reader.Fail("ProcessCode（工程コード）は必須です。");
                }
                if (sequence is not null && !sequences.Add(sequence.Value))
                {
                    reader.Fail($"品目 '{productCode}' の工程順序 {sequence} が重複しています。");
                }
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                steps.Add(new Routing
                {
                    ProductId = productId,
                    Sequence = sequence!.Value,
                    ProcessId = processId!.Value,
                    StandardWorkMinutes = work,
                    StandardSetupMinutes = setup,
                    RequiredSkillId = skillId,
                    EquipmentId = equipmentId,
                    // 代表設備も候補の1つとして扱う（候補を書かずに代表だけ指定した工順を移行するため）
                    EquipmentCandidates =
                    [
                        .. candidateIds.Concat(equipmentId is { } e ? [e] : []).Distinct()
                            .Select(x => new RoutingEquipment { EquipmentId = x }),
                    ],
                    ToolId = toolId,
                    WorkCenterId = workCenterId,
                    ChecklistId = checklistId,
                    ControlItems = controlItems,
                    WorkProcedureId = workProcedureId,
                });
            }
            if (failed)
            {
                continue;
            }

            var current = existing.Where(r => r.ProductId == productId).ToList();
            db.Routings.RemoveRange(current);
            db.Routings.AddRange(steps);
            if (current.Count > 0)
            {
                counter.Updated++;
            }
            else
            {
                counter.Created++;
            }
        }
    }

    // ---- ユーザー（システム管理者のみ）----

    /// <summary>工順CSVの候補設備列（セミコロン区切りの資産番号）を解決する</summary>
    private static List<int> ParseCandidates(
        CsvRowReader reader, CsvTable table, CsvRecord row, IReadOnlyDictionary<string, int> equipmentIds)
    {
        if (!table.HasColumn("EquipmentAssetNos"))
        {
            return [];
        }
        var raw = table.Value(row, "EquipmentAssetNos");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }
        var result = new List<int>();
        foreach (var code in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (equipmentIds.TryGetValue(code, out var id))
            {
                result.Add(id);
            }
            else
            {
                reader.Fail($"候補設備 '{code}' は登録されていません（EquipmentAssetNos）。");
            }
        }
        return result;
    }

    private async Task ImportInspectionDevicesAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.InspectionDevices.ToDictionaryAsync(d => d.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 30);
            if (reader.Failed || !CheckUnique(reader, seen, code, "検査機コード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var device);
            device ??= new InspectionDevice { Code = code };

            var name = reader.RequiredText("Name", 200);
            var serialNo = reader.Text("SerialNo", device.SerialNo, 100);
            var location = reader.Text("Location", device.Location, 200);
            var calibratedOn = reader.DateOrNull("CalibratedOn", device.CalibratedOn);
            var dueOn = reader.DateOrNull("CalibrationDueOn", device.CalibrationDueOn);
            var cycle = reader.IntOrNull("CalibrationCycleDays", device.CalibrationCycleDays, 1);
            var note = reader.Text("Note", device.Note, 500);
            var isActive = reader.Bool("IsActive", device.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            device.Name = name;
            device.SerialNo = serialNo;
            device.Location = location;
            device.CalibratedOn = calibratedOn;
            device.CalibrationDueOn = dueOn;
            device.CalibrationCycleDays = cycle;
            device.Note = note;
            device.IsActive = isActive;

            if (isNew)
            {
                db.InspectionDevices.Add(device);
                byCode[code] = device;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    private async Task ImportShiftsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.Shifts.ToDictionaryAsync(s => s.Code, StringComparer.Ordinal, ct);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // 所属者がいる直を無効化できないのは単票APIと同じ。行ごとに引けるよう先にまとめて数える
        // （ユーザーは別種別のCSVなので、この取込の途中で所属が変わることはない）
        var assignedUsers = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.ShiftId != null)
            .GroupBy(u => u.ShiftId!.Value)
            .Select(g => new { ShiftId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ShiftId, x => x.Count, ct);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var code = reader.RequiredText("Code", 20);
            if (reader.Failed || !CheckUnique(reader, seen, code, "シフトコード"))
            {
                continue;
            }

            var isNew = !byCode.TryGetValue(code, out var shift);
            shift ??= new Shift { Code = code };

            var name = reader.RequiredText("Name", 100);
            var start = ParseTime(reader, "StartTime", isNew ? null : shift.StartTime);
            var end = ParseTime(reader, "EndTime", isNew ? null : shift.EndTime);
            var isActive = reader.Bool("IsActive", shift.IsActive);
            if (reader.Failed || start is not { } startTime || end is not { } endTime)
            {
                if (!reader.Failed)
                {
                    reader.Fail("StartTime（開始時刻）とEndTime（終了時刻）は HH:mm で指定してください。");
                }
                continue;
            }
            // 時間帯の重なりは単票APIと同じ条件で弾く（重なると実績の直が一意に決まらない）
            var others = byCode.Values.Where(s => s.IsActive && !ReferenceEquals(s, shift)).ToList();
            if (isActive && ShiftSchedulePolicy.Check(code, startTime, endTime, others) is { } scheduleError)
            {
                reader.Fail(scheduleError);
                continue;
            }
            if (!isNew && shift.IsActive && !isActive
                && MasterDeactivationPolicy.CheckShift(
                    code, assignedUsers.GetValueOrDefault(shift.Id)) is { } inUse)
            {
                reader.Fail(inUse);
                continue;
            }

            shift.Name = name;
            shift.StartTime = startTime;
            shift.EndTime = endTime;
            shift.IsActive = isActive;

            if (isNew)
            {
                db.Shifts.Add(shift);
                byCode[code] = shift;
                counter.Created++;
            }
            else
            {
                counter.Updated++;
            }
        }
    }

    /// <summary>HH:mm の時刻列を読む（空欄なら既定値。TimeOnlyを読む列はここだけ）</summary>
    private static TimeOnly? ParseTime(CsvRowReader reader, string column, TimeOnly? fallback)
    {
        var text = reader.Text(column, null, 10);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }
        if (TimeOnly.TryParse(text, out var parsed))
        {
            return parsed;
        }
        reader.Fail($"{column} は HH:mm 形式で指定してください（'{text}'）。");
        return null;
    }

    private async Task ImportUsersAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 作業場所は段を問わない（Spec.md 5.7）
        var workCenters = await db.WorkCenters.AsNoTracking()
            .ToDictionaryAsync(w => w.Code, StringComparer.Ordinal, ct);
        // 無効な直は所属先にしない（単票APIと同じ条件）
        var shiftIds = await db.Shifts.AsNoTracking().Where(s => s.IsActive)
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.Ordinal, ct);

        foreach (var row in table.Rows)
        {
            var reader = new CsvRowReader(table, row, errors);
            var userName = reader.RequiredText("UserName", 100);
            if (reader.Failed || !CheckUnique(reader, seen, userName, "ユーザー名"))
            {
                continue;
            }

            var user = await userManager.FindByNameAsync(userName);
            var isNew = user is null;
            var displayName = reader.RequiredText("DisplayName", 100);
            var isActive = reader.Bool("IsActive", user?.IsActive ?? true);
            var password = reader.Text("InitialPassword", null);
            var workCenter = ResolveWorkCenter(
                reader, table, "WorkCenterCode", user?.WorkCenterId, workCenters, out var workCenterKept);
            // 列が無ければ現状維持、空欄なら解除（作業場所と同じ扱い）
            var departmentKept = !table.HasColumn("Department");
            var department = departmentKept ? user?.Department : reader.Text("Department", null, 100);
            var shiftKept = !table.HasColumn("ShiftCode");
            var shiftId = shiftKept
                ? user?.ShiftId
                : reader.Reference("ShiftCode", null, shiftIds, "直");
            // Roles列が無ければ現状維持、空欄なら全ロール解除
            var roles = table.HasColumn("Roles") ? ParseRoles(reader, table.Value(row, "Roles")) : null;
            if (reader.Failed)
            {
                continue;
            }
            if (!workCenterKept && WorkCenterHierarchyPolicy.CheckLocationPlacement(workCenter) is { } wcReason)
            {
                reader.Fail(wcReason);
                continue;
            }

            if (isNew)
            {
                if (password is null)
                {
                    reader.Fail($"新規ユーザー '{userName}' には InitialPassword（初期パスワード）が必要です。");
                    continue;
                }
                user = new AppUser
                {
                    UserName = userName,
                    DisplayName = displayName,
                    IsActive = isActive,
                    WorkCenterId = workCenterKept ? null : workCenter?.Id,
                    Department = department,
                    ShiftId = shiftId,
                    // 管理者が発行した初期パスワードは初回ログイン時に変更を強制する
                    MustChangePassword = true,
                };
                var created = await userManager.CreateAsync(user, password);
                if (!ReportIdentityErrors(reader, created, userName))
                {
                    continue;
                }
                if (roles is not null && roles.Count > 0)
                {
                    await userManager.AddToRolesAsync(user, roles);
                }
                counter.Created++;
                continue;
            }

            var deactivated = user!.IsActive && !isActive;
            user.DisplayName = displayName;
            user.IsActive = isActive;
            if (!workCenterKept)
            {
                user.WorkCenterId = workCenter?.Id;
            }
            if (!departmentKept)
            {
                user.Department = department;
            }
            if (!shiftKept)
            {
                user.ShiftId = shiftId;
            }
            await userManager.UpdateAsync(user);

            if (roles is not null)
            {
                var currentRoles = await userManager.GetRolesAsync(user);
                await userManager.RemoveFromRolesAsync(user, currentRoles.Except(roles));
                await userManager.AddToRolesAsync(user, roles.Except(currentRoles));
            }
            if (password is not null)
            {
                // CSVでパスワードを指定した場合はリセット扱い（次回ログイン時に変更を強制）
                var token = await userManager.GeneratePasswordResetTokenAsync(user);
                var reset = await userManager.ResetPasswordAsync(user, token, password);
                if (!ReportIdentityErrors(reader, reset, userName))
                {
                    continue;
                }
                user.MustChangePassword = true;
                await userManager.UpdateAsync(user);
            }
            if (deactivated || password is not null)
            {
                // 無効化・パスワード変更時は既存セッションを失効させる（Spec.md 7.4）
                await refreshTokenService.RevokeAllForUserAsync(user.Id, ct);
            }
            counter.Updated++;
        }

        // 「Aを降格してからBを昇格する」順序を誤って弾かないよう、全行を適用したあとに確認する。
        // エラーを立てれば取込全体がロールバックされる
        if (errors.Count == 0 && !await LastAdminPolicy.HasActiveAdminAsync(userManager))
        {
            errors.Add(new CsvImportError(0, LastAdminPolicy.NoAdminRemains));
        }
    }

    private async Task ImportUserSkillsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var userIds = await db.Users.AsNoTracking()
            .Where(u => u.UserName != null)
            .ToDictionaryAsync(u => u.UserName!, u => u.Id, StringComparer.OrdinalIgnoreCase, ct);
        var skillIds = await db.Skills.AsNoTracking()
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.Ordinal, ct);
        var existing = await db.UserSkills.ToListAsync(ct);

        foreach (var group in GroupRows(table, "UserName", errors))
        {
            var userReader = new CsvRowReader(table, group.First(), errors);
            var userName = userReader.RequiredText("UserName");
            if (!userIds.TryGetValue(userName, out var userId))
            {
                userReader.Fail($"ユーザー '{userName}' は登録されていません。先にユーザーを取り込んでください。");
                continue;
            }

            var assignments = new List<UserSkill>();
            var skills = new HashSet<int>();
            var failed = false;
            foreach (var row in group)
            {
                var reader = new CsvRowReader(table, row, errors);
                var skillCode = reader.RequiredText("SkillCode");
                var skillId = reader.Reference("SkillCode", null, skillIds, "スキル・資格");
                var acquiredOn = reader.DateOrNull("AcquiredOn", null);
                var expiresOn = reader.DateOrNull("ExpiresOn", null);
                if (skillId is not null && !skills.Add(skillId.Value))
                {
                    reader.Fail($"ユーザー '{userName}' にスキル '{skillCode}' が重複しています。");
                }
                if (reader.Failed)
                {
                    failed = true;
                    continue;
                }
                assignments.Add(new UserSkill
                {
                    UserId = userId,
                    SkillId = skillId!.Value,
                    AcquiredOn = acquiredOn,
                    ExpiresOn = expiresOn,
                });
            }
            if (failed)
            {
                continue;
            }

            var current = existing.Where(s => s.UserId == userId).ToList();
            db.UserSkills.RemoveRange(current);
            db.UserSkills.AddRange(assignments);
            if (current.Count > 0)
            {
                counter.Updated++;
            }
            else
            {
                counter.Created++;
            }
        }
    }

    // ---- 共通ヘルパー ----

    private async Task<Dictionary<string, int>> ProductIdsAsync(CancellationToken ct) =>
        await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Code, p => p.Id, StringComparer.Ordinal, ct);

    /// <summary>同一キーの行をファイル出現順にまとめる（キーが空欄の行はエラー）</summary>
    private static List<List<CsvRecord>> GroupRows(CsvTable table, string keyColumn, List<CsvImportError> errors)
    {
        var groups = new List<List<CsvRecord>>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in table.Rows)
        {
            var key = table.Value(row, keyColumn);
            if (key is null)
            {
                errors.Add(new CsvImportError(row.Line, $"{keyColumn} は必須です。"));
                continue;
            }
            if (!index.TryGetValue(key, out var position))
            {
                position = groups.Count;
                index[key] = position;
                groups.Add([]);
            }
            groups[position].Add(row);
        }
        return groups;
    }

    private static bool CheckUnique(CsvRowReader reader, HashSet<string> seen, string key, string label)
    {
        if (seen.Add(key))
        {
            return true;
        }
        reader.Fail($"{label} '{key}' が複数行にあります。");
        return false;
    }

    private static List<string> ParseRoles(CsvRowReader reader, string? value)
    {
        if (value is null)
        {
            return [];
        }
        var roles = value.Split([';', ',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => MesRoles.All.FirstOrDefault(known => string.Equals(known, r, StringComparison.OrdinalIgnoreCase)) ?? r)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unknown = roles.Except(MesRoles.All, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            reader.Fail($"不明なロールが含まれています：{string.Join(", ", unknown)}"
                + $"（指定可能：{string.Join(" / ", MesRoles.All)}）");
        }
        return roles;
    }

    private static bool ReportIdentityErrors(
        CsvRowReader reader, Microsoft.AspNetCore.Identity.IdentityResult result, string userName)
    {
        if (result.Succeeded)
        {
            return true;
        }
        foreach (var error in result.Errors)
        {
            reader.Fail($"ユーザー '{userName}'：{error.Description}");
        }
        return false;
    }
}
