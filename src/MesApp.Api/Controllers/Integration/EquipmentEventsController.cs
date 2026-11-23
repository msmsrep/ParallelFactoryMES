using System.Security.Claims;
using MesApp.Api.Localization;
using MesApp.Api.Services;
using MesApp.Core.Contracts.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MesApp.Api.Controllers.Integration;

/// <summary>
/// 連携API の設備イベントの受信（Spec.md 3.5・3.8 連携API。docs-dev/EquipmentEventTasks.md EQE-02。B-40-20-01〜02、E-20-10-01）。
/// <para>
/// PLC との通信は MES に持たない。ゲートウェイ（Node-RED など）が設備の状態変化を受けて、まとめてここへ送る。
/// MES は設備ごとに発生時刻の順に並べ、設備稼働履歴の区間に組み立てる。対象は設備マスタで
/// 「設備イベントで収集する」を立てた設備だけ。権限はロールを問わない（設備稼働報告は手入力でもロールで絞らない）
/// </para>
/// </summary>
[ApiController]
[Route("api/integration/v1")]
[Authorize(AuthenticationSchemes = ApiKeyAuthentication.IntegrationSchemes)]
[ApiExplorerSettings(GroupName = IntegrationOpenApi.DocumentName)]
public class EquipmentEventsController(EquipmentEventService events) : ControllerBase
{
    /// <summary>
    /// 設備の状態変化をまとめて受け取る。1件ごとに Accepted / Duplicate / Rejected を返し、1件の不正で他の件を捨てない
    /// （全件が拒否でも 200）。同じ eventId の送り直しは Duplicate になる。最後に反映した時刻より前の件（遅着）は拒否する
    /// </summary>
    [HttpPost("equipment-events")]
    public async Task<ActionResult<EquipmentEventBatchResponse>> Receive(
        EquipmentEventBatchRequest request, CancellationToken ct)
    {
        if (request.Events is not { Count: > 0 } items)
        {
            return this.BadRequestProblem(ApiText.T("events に1件以上のイベントを入れてください。"));
        }
        if (items.Count > events.MaxBatchSize)
        {
            return this.BadRequestProblem(ApiText.T("1回に送れるイベントは {0} 件までです。", events.MaxBatchSize));
        }
        return await events.ReceiveAsync(items, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
    }
}
