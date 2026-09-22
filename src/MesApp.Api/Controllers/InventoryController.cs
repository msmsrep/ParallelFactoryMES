using MesApp.Api.Localization;
using System.Security.Claims;
using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Controllers;

/// <summary>
/// 在庫オペレーション（D-10-30、D-30-10、D-40-40 共通）：照会・移動・調整・ステータス変更・
/// 分割/統合・振替・廃棄・返品・払出戻し・期限管理。参照は認証済み全員、更新は在庫管理ロール。
/// </summary>
[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController(
    MesAppDbContext db,
    LotOperationService lotOperations,
    IBusinessDateService businessDate) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    // ---- 照会（D-10-30-01：品目別・ロケーション別・ロット別）----

    [HttpGet("stocks")]
    public async Task<ActionResult<PagedResult<StockResponse>>> Stocks(
        [FromQuery] PageQuery paging,
        [FromQuery] int? productId = null,
        [FromQuery] int? locationId = null,
        [FromQuery] int? lotId = null,
        [FromQuery] bool includeEmpty = false,
        CancellationToken ct = default)
    {
        var query = db.InventoryStocks.AsNoTracking().AsQueryable();
        if (productId is not null)
        {
            query = query.Where(s => s.ProductId == productId);
        }
        if (locationId is not null)
        {
            query = query.Where(s => s.LocationId == locationId);
        }
        if (lotId is not null)
        {
            query = query.Where(s => s.LotId == lotId);
        }
        if (!includeEmpty)
        {
            query = query.Where(s => s.Quantity > 0);
        }
        return await query
            .OrderBy(s => s.Product!.Code).ThenBy(s => s.Lot!.LotNumber).ThenBy(s => s.Location!.Code)
            .Select(s => new StockResponse(
                s.Id, s.ProductId, s.Product!.Code, s.Product!.Name,
                s.LotId, s.Lot!.LotNumber, s.Lot!.StockStatus, s.Lot!.ExpiresOn,
                s.LocationId, s.Location!.Code, s.Quantity))
            .ToPagedResultAsync(paging, ct);
    }

    /// <summary>
    /// ロット選択用の選択肢（検査指示・不適合起票・出荷判定などの対象ロット指定）。
    /// 一覧（stocks）と違いページを送らず、ロット番号・品目コード/名称の部分一致で絞り込む。
    /// スキャンしたロット番号をそのまま q に渡せる。
    /// </summary>
    [HttpGet("stocks/options")]
    public async Task<ActionResult<OptionsResult<StockResponse>>> StockOptions(
        [FromQuery] OptionQuery options,
        // 在庫0のロットも含める（トレーサビリティのロット番号解決で使う）
        [FromQuery] bool includeEmpty = false,
        CancellationToken ct = default)
    {
        var query = db.InventoryStocks.AsNoTracking().AsQueryable();
        if (!includeEmpty)
        {
            query = query.Where(s => s.Quantity > 0);
        }
        if (options.Keyword is { } keyword)
        {
            query = query.Where(s =>
                s.Lot!.LotNumber.Contains(keyword)
                || s.Product!.Code.Contains(keyword)
                || s.Product!.Name.Contains(keyword));
        }
        return await query
            .OrderBy(s => s.Lot!.LotNumber).ThenBy(s => s.Location!.Code)
            .Select(s => new StockResponse(
                s.Id, s.ProductId, s.Product!.Code, s.Product!.Name,
                s.LotId, s.Lot!.LotNumber, s.Lot!.StockStatus, s.Lot!.ExpiresOn,
                s.LocationId, s.Location!.Code, s.Quantity))
            .ToOptionsResultAsync(options, ct);
    }

    /// <summary>滞留在庫の期限管理・アラート（D-10-30-09。有効期限が指定日数以内または超過の在庫）</summary>
    [HttpGet("expiring")]
    public async Task<ActionResult<List<StockResponse>>> Expiring(
        [FromQuery] int withinDays = 30, CancellationToken ct = default)
    {
        var threshold = businessDate.Today.AddDays(withinDays);
        return await db.InventoryStocks.AsNoTracking()
            .Where(s => s.Quantity > 0 && s.Lot!.ExpiresOn != null && s.Lot!.ExpiresOn <= threshold)
            .OrderBy(s => s.Lot!.ExpiresOn)
            .Select(s => new StockResponse(
                s.Id, s.ProductId, s.Product!.Code, s.Product!.Name,
                s.LotId, s.Lot!.LotNumber, s.Lot!.StockStatus, s.Lot!.ExpiresOn,
                s.LocationId, s.Location!.Code, s.Quantity))
            .ToListAsync(ct);
    }

    [HttpGet("transactions")]
    public async Task<ActionResult<PagedResult<TransactionResponse>>> Transactions(
        [FromQuery] PageQuery paging,
        [FromQuery] int? lotId = null,
        [FromQuery] int? productId = null,
        [FromQuery] int? workOrderId = null,
        CancellationToken ct = default)
    {
        var query = db.InventoryTransactions.AsNoTracking().AsQueryable();
        if (lotId is not null)
        {
            query = query.Where(t => t.LotId == lotId);
        }
        if (productId is not null)
        {
            query = query.Where(t => t.ProductId == productId);
        }
        if (workOrderId is not null)
        {
            query = query.Where(t => t.WorkOrderId == workOrderId);
        }
        return await query.OrderByDescending(t => t.Id)
            .Select(t => new TransactionResponse(
                t.Id, t.Type, t.ProductId, t.Product!.Code,
                t.LotId, t.Lot!.LotNumber, t.Quantity,
                t.FromLocationId, t.FromLocation!.Code, t.ToLocationId, t.ToLocation!.Code,
                t.WorkOrderId, t.Timestamp, t.Note))
            .ToPagedResultAsync(paging, ct);
    }

    [HttpGet("lots/{id:int}")]
    public async Task<ActionResult<LotResponse>> GetLot(int id, CancellationToken ct)
    {
        var lot = await db.Lots.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new LotResponse(
                l.Id, l.LotNumber, l.ProductId, l.Product!.Code, l.Product!.Name,
                l.InitialQuantity, l.OriginType, l.StockStatus,
                l.ManufacturedOn, l.ExpiresOn, l.Grade, l.ParentLotId))
            .FirstOrDefaultAsync(ct);
        return lot is null ? NotFound() : lot;
    }

    // ---- 更新系（在庫管理ロール）----
    // 業務処理は LotOperationService に集約し、ここでは結果を ProblemDetails に変換するだけにする

    /// <summary>在庫移動（D-10-30-02）</summary>
    [HttpPost("move")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<IActionResult> Move(MoveRequest request, CancellationToken ct) =>
        ToResult(await lotOperations.MoveAsync(request.LotId, request.FromLocationId,
            request.ToLocationId, request.Quantity, CurrentUserId, ct));

    /// <summary>数量調整（実在庫との差異訂正 D-10-30-04。理由必須・監査ログ記録）</summary>
    [HttpPost("adjust")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<IActionResult> Adjust(AdjustRequest request, CancellationToken ct) =>
        ToResult(await lotOperations.AdjustAsync(request.LotId, request.LocationId,
            request.NewQuantity, request.Reason, CurrentUserId, ct));

    /// <summary>在庫ステータス変更（保留・検査待ち・不良・廃棄予定等。D-10-30-08。ロット単位）</summary>
    [HttpPost("status")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<IActionResult> ChangeStatus(LotStatusRequest request, CancellationToken ct) =>
        ToResult(await lotOperations.ChangeStatusAsync(request.LotId, request.Status,
            request.Reason, CurrentUserId, ct));

    /// <summary>ロット分割（D-10-30-05。新ロットは親ロットの系譜・期限を引き継ぐ）</summary>
    [HttpPost("split")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<LotResponse>> Split(SplitRequest request, CancellationToken ct) =>
        ToLotResult(await lotOperations.SplitAsync(request.LotId, request.LocationId, request.Quantity,
            request.NewLotNumber, CurrentUserId, ct));

    /// <summary>ロット統合（D-10-30-05。同一品目・同一ロケーションの在庫を統合先ロットへ移す）</summary>
    [HttpPost("merge")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<IActionResult> Merge(MergeRequest request, CancellationToken ct) =>
        ToResult(await lotOperations.MergeAsync(request.SourceLotId, request.TargetLotId,
            request.LocationId, CurrentUserId, ct));

    /// <summary>品目振替・ロット振替（D-10-30-06〜07。新しいロットを生成して数量を移す）</summary>
    [HttpPost("transfer")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<ActionResult<LotResponse>> Transfer(LotTransferRequest request, CancellationToken ct) =>
        ToLotResult(await lotOperations.TransferAsync(request.LotId, request.LocationId, request.Quantity,
            request.NewProductId, request.NewLotNumber, CurrentUserId, ct));

    /// <summary>在庫廃棄（D-50-30-01）</summary>
    [HttpPost("discard")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<IActionResult> Discard(DiscardRequest request, CancellationToken ct) =>
        ToResult(await lotOperations.DiscardAsync(request.LotId, request.LocationId,
            request.Quantity, request.Reason, CurrentUserId, ct));

    /// <summary>返品（D-10-10-05。サプライヤーへの返品による在庫引落し）</summary>
    [HttpPost("return")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<IActionResult> Return(ReturnRequest request, CancellationToken ct) =>
        ToResult(await lotOperations.ReturnAsync(request.LotId, request.LocationId,
            request.Quantity, request.Reason, CurrentUserId, ct));

    /// <summary>払出戻し（D-20-20-03。工程に払い出した部材の在庫戻し）</summary>
    [HttpPost("issue-return")]
    [Authorize(Roles = MesRoleGroups.InventoryManage)]
    public async Task<IActionResult> IssueReturn(IssueReturnRequest request, CancellationToken ct) =>
        ToResult(await lotOperations.IssueReturnAsync(request.LotId, request.LocationId,
            request.Quantity, request.WorkOrderId, CurrentUserId, ct));

    private IActionResult ToResult(Outcome<Lot> outcome) =>
        outcome.Failed ? ToProblem(outcome) : NoContent();

    private ActionResult<LotResponse> ToLotResult(Outcome<Lot> outcome)
    {
        if (outcome.Failed)
        {
            return ToProblem(outcome);
        }
        var (lot, product) = (outcome.Value!, outcome.Value!.Product!);
        return new LotResponse(lot.Id, lot.LotNumber, product.Id, product.Code, product.Name,
            lot.InitialQuantity, lot.OriginType, lot.StockStatus,
            lot.ManufacturedOn, lot.ExpiresOn, lot.Grade, lot.ParentLotId);
    }

    private ActionResult ToProblem(Outcome<Lot> outcome) => outcome.Kind switch
    {
        OutcomeError.NotFound => NotFound(),
        OutcomeError.Conflict => this.ConflictProblem(outcome.Error),
        _ => this.BadRequestProblem(outcome.Error),
    };


    // ---- 倉庫業務進捗（D-50-30-07）----

    /// <summary>
    /// 倉庫業務の進捗（D-50-30-07）。指示したものがどれだけ片付いたかを業務種別ごとに返す。
    /// <para>
    /// 新しいエンティティは持たず、既存の指示を数え直すだけにする（実績は既に貯まっている）。
    /// **受入は対象にしない**——受入には「指示」が無く実施の記録だけなので、
    /// 件数を並べても進捗にならず、他の行と同じ意味で読めなくなる。
    /// 滞留を見るため、未完了のうち最も古いものの経過日数を併記する。
    /// </para>
    /// </summary>
    [HttpGet("warehouse-progress")]
    public async Task<ActionResult<WarehouseProgressResponse>> WarehouseProgress(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        var fromDate = from ?? businessDate.Today.AddDays(-6);
        var toDate = to ?? businessDate.Today;
        var start = businessDate.GetRange(fromDate).Start;
        var end = businessDate.GetRange(toDate).End;

        // SQLiteはDateTimeOffsetの比較を翻訳できないため、取得してから絞る（Spec.md 7.5 の例外）
        var picking = await db.PickingOrders.AsNoTracking()
            .Select(x => new { x.CreatedAt, Open = x.Status == PickingOrderStatus.Instructed,
                               Canceled = x.Status == PickingOrderStatus.Canceled })
            .ToListAsync(ct);
        var shipping = await db.ShippingOrders.AsNoTracking()
            .Select(x => new { x.CreatedAt, Open = x.Status == ShippingOrderStatus.Instructed,
                               Canceled = x.Status == ShippingOrderStatus.Canceled })
            .ToListAsync(ct);
        var transfer = await db.TransferOrders.AsNoTracking()
            .Select(x => new { x.CreatedAt, Open = x.Status == TransferOrderStatus.Instructed,
                               Canceled = x.Status == TransferOrderStatus.Canceled })
            .ToListAsync(ct);
        var stocktake = await db.Stocktakes.AsNoTracking()
            .Select(x => new { x.CreatedAt, Open = x.Status == StocktakeStatus.Instructed,
                               Canceled = x.Status == StocktakeStatus.Canceled })
            .ToListAsync(ct);

        List<WarehouseProgressRow> rows =
        [
            Row(ApiText.T("出庫ピッキング"), picking.Select(x => (x.CreatedAt, x.Open, x.Canceled))),
            Row(ApiText.T("出荷"), shipping.Select(x => (x.CreatedAt, x.Open, x.Canceled))),
            Row(ApiText.T("在庫移動"), transfer.Select(x => (x.CreatedAt, x.Open, x.Canceled))),
            Row(ApiText.T("棚卸"), stocktake.Select(x => (x.CreatedAt, x.Open, x.Canceled))),
        ];
        return new WarehouseProgressResponse(fromDate, toDate, rows);

        WarehouseProgressRow Row(
            string kind, IEnumerable<(DateTimeOffset CreatedAt, bool Open, bool Canceled)> source)
        {
            // 取消は指示が無かったのと同じ扱いにする（分母にも完了にも数えない）
            var items = source
                // 終端は翌製造日の開始時刻なので半開区間で切る（他の期間APIと同じ）。
                // 閉区間にすると境界ちょうどに作られた指示が前日と当日の両方に数えられる
                .Where(x => !x.Canceled && x.CreatedAt >= start && x.CreatedAt < end)
                .ToList();
            var open = items.Where(x => x.Open).ToList();
            // 経過日数は製造日同士で引く。作成日時をUTCの暦日にすると、工場の時刻とUTCで日付がずれる
            // 時間帯（日本なら0〜9時）の指示が1日古く数えられ、境界時刻（既定6時）とも食い違う
            var oldest = open.Count == 0
                ? (int?)null
                : businessDate.Today.DayNumber
                  - businessDate.GetBusinessDate(open.Min(x => x.CreatedAt)).DayNumber;
            return new WarehouseProgressRow(
                kind, items.Count, items.Count - open.Count, open.Count, oldest);
        }
    }
}
