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
                case MasterCsvKinds.Tools:
                    await ImportToolsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.Locations:
                    await ImportLocationsAsync(table, errors, counter, ct);
                    break;
                case MasterCsvKinds.InspectionItems:
                    await ImportInspectionItemsAsync(table, errors, counter, ct);
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
            var status = reader.Enum("Status", equipment.Status, CsvEnumLabels.EquipmentStatuses);
            var maintenanceType = reader.Enum("MaintenanceType", equipment.MaintenanceType, CsvEnumLabels.MaintenanceTypes);
            var threshold = reader.NumberOrNull("MaintenanceThreshold", equipment.MaintenanceThreshold, 0);
            var parts = reader.Text("MaintenanceParts", equipment.MaintenanceParts);
            var isActive = reader.Bool("IsActive", equipment.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            equipment.Name = name;
            equipment.Site = site;
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

    private async Task ImportLocationsAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var byCode = await db.Locations.ToDictionaryAsync(l => l.Code, StringComparer.Ordinal, ct);
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
            var shelfNo = reader.Text("ShelfNo", location.ShelfNo, 50);
            var isActive = reader.Bool("IsActive", location.IsActive);
            if (reader.Failed)
            {
                continue;
            }

            location.AreaType = areaType;
            location.ShelfNo = shelfNo;
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
                var toolId = reader.Reference("ToolCode", null, toolIds, "治工具");
                var checklistId = reader.Reference("ChecklistCode", null, checklistIds, "チェックリスト");
                var controlItems = reader.Text("ControlItems", null, 500);
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
                    ToolId = toolId,
                    ChecklistId = checklistId,
                    ControlItems = controlItems,
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

    private async Task ImportUsersAsync(
        CsvTable table, List<CsvImportError> errors, ImportCounter counter, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
            // Roles列が無ければ現状維持、空欄なら全ロール解除
            var roles = table.HasColumn("Roles") ? ParseRoles(reader, table.Value(row, "Roles")) : null;
            if (reader.Failed)
            {
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
