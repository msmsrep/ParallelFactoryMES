using System.Linq.Expressions;
using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// サンプル品の保管管理（Spec.md 3.4 その他。D-40-50-01）。
/// 検査で採取したサンプルは保管期限まで捨てられないため、所在と期限を追う。
/// サンプル取得・識別ラベル（C-20-40-03）は検査側の話で、こちらは**取得後の保管**を扱う。
/// </summary>
[ApiController]
[Route("api/sample-storages")]
[Authorize]
public class SampleStoragesController(
    MesAppDbContext db,
    SampleStorageService samples,
    IBusinessDateService businessDate) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>サンプルの一覧（状態・品目・保管期限で絞り込む）</summary>
    [HttpGet]
    public async Task<ActionResult<List<SampleStorageResponse>>> List(
        [FromQuery] SampleStorageStatus? status = null,
        [FromQuery] int? productId = null,
        [FromQuery] bool retentionOverOnly = false,
        CancellationToken ct = default)
    {
        var query = db.SampleStorages.AsNoTracking().AsQueryable();
        if (status is not null)
        {
            query = query.Where(x => x.Status == status);
        }
        if (productId is not null)
        {
            query = query.Where(x => x.ProductId == productId);
        }
        if (retentionOverOnly)
        {
            // 「もう処分してよいもの」を出す。保管中のものだけが対象
            var today = businessDate.Today;
            query = query.Where(x => x.Status == SampleStorageStatus.Stored
                                     && x.RetainUntil != null && x.RetainUntil < today);
        }
        var rows = await query.OrderByDescending(x => x.Id).Select(Projection).ToListAsync(ct);
        return rows.Select(Fill).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SampleStorageResponse>> Get(int id, CancellationToken ct)
    {
        var row = await db.SampleStorages.AsNoTracking()
            .Where(x => x.Id == id).Select(Projection).FirstOrDefaultAsync(ct);
        return row is null ? NotFound() : Fill(row);
    }

    /// <summary>
    /// サンプルの採取（D-40-50-01）。採取した分は在庫から抜く（判定・保存は実績CSV取込と共通の SampleStorageService）
    /// </summary>
    [HttpPost]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<SampleStorageResponse>> Collect(
        SampleCollectRequest request, CancellationToken ct)
    {
        var outcome = await samples.CollectAsync(request, null, CurrentUserId, ct);
        if (outcome.Failed)
        {
            return outcome.Kind == OutcomeError.NotFound
                ? this.NotFoundProblem(outcome.Error)
                : this.BadRequestProblem(outcome.Error);
        }
        var id = outcome.Value!.Id;
        return CreatedAtAction(nameof(Get), new { id }, await LoadAsync(id, ct));
    }

    /// <summary>保管の終了（払出・廃棄）。在庫は動かさない（採取時に既に抜いてあるため）</summary>
    [HttpPost("{id:int}/close")]
    [Authorize(Roles = MesRoleGroups.QualityManage)]
    public async Task<ActionResult<SampleStorageResponse>> Close(
        int id, SampleCloseRequest request, CancellationToken ct)
    {
        var outcome = await samples.CloseAsync(id, request, CurrentUserId, ct);
        return outcome.Kind switch
        {
            OutcomeError.None => await LoadAsync(id, ct),
            OutcomeError.NotFound => NotFound(),
            OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
            _ => this.BadRequestProblem(outcome.Error),
        };
    }

    private async Task<SampleStorageResponse> LoadAsync(int id, CancellationToken ct) =>
        Fill(await db.SampleStorages.AsNoTracking()
            .Where(x => x.Id == id).Select(Projection).FirstAsync(ct));

    /// <summary>
    /// 期限の判定は業務日付基準で、SQLへ持ち込まず取得後に埋める
    /// （検査機の校正期限 C-20-50-03 と同じ形）
    /// </summary>
    private SampleStorageResponse Fill(SampleStorageResponse row)
    {
        if (row.RetainUntil is not { } until)
        {
            return row;
        }
        var days = until.DayNumber - businessDate.Today.DayNumber;
        return row with
        {
            IsRetentionOver = row.Status == SampleStorageStatus.Stored && days < 0,
            DaysUntilRetentionEnd = days,
        };
    }

    /// <summary>一覧・単票で共通の射影。EF Core が翻訳できるよう**式として持つ**</summary>
    private static readonly Expression<Func<SampleStorage, SampleStorageResponse>> Projection =
        x => new SampleStorageResponse(
            x.Id, x.SampleNo,
            x.ProductId, x.Product!.Code, x.Product!.Name, x.Product!.Unit,
            x.LotId, x.Lot!.LotNumber,
            x.InspectionOrderId, x.InspectionOrder == null ? null : x.InspectionOrder.OrderNo,
            x.Quantity,
            x.StorageLocationId, x.StorageLocation!.Code,
            x.CollectedOn, x.RetainUntil, x.Status,
            x.ClosedOn, x.Note,
            false, null);
}
