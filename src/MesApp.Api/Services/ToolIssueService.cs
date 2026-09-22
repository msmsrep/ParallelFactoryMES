using MesApp.Core.Localization;
using MesApp.Api.Localization;
using MesApp.Api.Policies;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Maintenance;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 治工具の引当・払出・返却・取消（Spec.md 3.2 前段取り。B-20-30-01〜03）。
/// 現物の所在を扱うものであり、寿命の累計（<c>api/tool-usages</c>。E-60-20-01）とは別物。
/// 使えるかどうかの判定は <see cref="ToolIssuePolicy"/> に置き、ここは集計と状態の反映だけを行う。
/// <para>保存と監査ログまで行う。状態は Controller で代入せず本サービス経由で変更する。</para>
/// </summary>
public sealed class ToolIssueService(MesAppDbContext db, IAuditLogger auditLogger)
{
    /// <summary>引当（B-20-30-01）。使えない治工具は ToolIssuePolicy が弾く</summary>
    public async Task<Outcome<ToolIssue>> AllocateAsync(
        ToolAllocateRequest request, string? userId, CancellationToken ct)
    {
        var tool = await db.Tools.FindAsync([request.ToolId], ct);
        if (tool is null)
        {
            return Outcome<ToolIssue>.NotFound(ApiText.T("治工具ID {0} は登録されていません。", request.ToolId));
        }
        var workOrder = await db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == request.WorkOrderId, ct);
        if (workOrder is null)
        {
            return Outcome<ToolIssue>.NotFound(ApiText.T("作業指示ID {0} は登録されていません。", request.WorkOrderId));
        }

        if (await CheckIssuableAsync(tool, ct) is { } reason)
        {
            return Outcome<ToolIssue>.Conflict(reason);
        }

        var issue = new ToolIssue
        {
            ToolId = tool.Id,
            WorkOrderId = workOrder.Id,
            Status = ToolIssueStatus.Allocated,
            AllocatedByUserId = userId,
            Note = request.Note,
        };
        db.ToolIssues.Add(issue);
        // 引当た時点で他の作業指示から引けなくする（現物は1つしかない）
        tool.Status = ToolStatus.InUse;
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Execution", "ToolAllocate", nameof(ToolIssue), issue.Id.ToString(),
            detail: $"tool={tool.Code} workOrder={workOrder.WorkOrderNo}", ct: ct);
        return Outcome<ToolIssue>.Ok(issue);
    }

    /// <summary>払出・受領確認（B-20-30-02〜03）。渡す直前にもう一度使えるかを見る</summary>
    public async Task<Outcome<ToolIssue>> IssueAsync(
        int id, ToolIssueReceiveRequest request, string? userId, CancellationToken ct)
    {
        var issue = await db.ToolIssues.Include(i => i.Tool).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null)
        {
            return Outcome<ToolIssue>.NotFound(ApiText.T("引当ID {0} は登録されていません。", id));
        }
        if (issue.Status != ToolIssueStatus.Allocated)
        {
            return Outcome<ToolIssue>.Conflict(ApiText.T("状態 '{0}' の引当は払い出せません。", EnumLabels.Of(issue.Status)));
        }

        // 引当てから払出までの間に寿命へ達している／メンテへ入っていることがある
        if (await CheckIssuableAsync(issue.Tool!, ct, ignoreIssueId: issue.Id) is { } reason)
        {
            return Outcome<ToolIssue>.Conflict(reason);
        }

        var receivedBy = request.IssuedToUserId ?? userId;
        if (receivedBy is not null && !await db.Users.AnyAsync(u => u.Id == receivedBy, ct))
        {
            return Outcome<ToolIssue>.Invalid(ApiText.T("受領者が登録されていません。"));
        }

        issue.Status = ToolIssueStatus.Issued;
        issue.IssuedAt = DateTimeOffset.UtcNow;
        issue.IssuedToUserId = receivedBy;
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Execution", "ToolIssue", nameof(ToolIssue), id.ToString(),
            detail: $"tool={issue.Tool!.Code} receivedBy={receivedBy}", ct: ct);
        return Outcome<ToolIssue>.Ok(issue);
    }

    /// <summary>返却（使用を終えて戻す）。治工具は再び引当可能になる</summary>
    public async Task<Outcome<ToolIssue>> ReturnAsync(
        int id, ToolIssueCloseRequest request, string? userId, CancellationToken ct)
    {
        var issue = await db.ToolIssues.Include(i => i.Tool).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null)
        {
            return Outcome<ToolIssue>.NotFound(ApiText.T("引当ID {0} は登録されていません。", id));
        }
        if (issue.Status is not (ToolIssueStatus.Allocated or ToolIssueStatus.Issued))
        {
            return Outcome<ToolIssue>.Conflict(ApiText.T("状態 '{0}' の引当は返却できません。", EnumLabels.Of(issue.Status)));
        }

        issue.Status = ToolIssueStatus.Returned;
        issue.ReturnedAt = DateTimeOffset.UtcNow;
        issue.ReturnedByUserId = userId;
        issue.Note = request.Note ?? issue.Note;
        await RestoreToolStatusAsync(issue, ct);
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Execution", "ToolReturn", nameof(ToolIssue), id.ToString(),
            detail: $"tool={issue.Tool!.Code}", ct: ct);
        return Outcome<ToolIssue>.Ok(issue);
    }

    /// <summary>引当の取消（払出前のみ）</summary>
    public async Task<Outcome<ToolIssue>> CancelAsync(
        int id, ToolIssueCloseRequest request, CancellationToken ct)
    {
        var issue = await db.ToolIssues.Include(i => i.Tool).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (issue is null)
        {
            return Outcome<ToolIssue>.NotFound(ApiText.T("引当ID {0} は登録されていません。", id));
        }
        if (issue.Status != ToolIssueStatus.Allocated)
        {
            return Outcome<ToolIssue>.Conflict(ApiText.T("払出済みの引当は取り消せません。返却で戻してください。"));
        }

        issue.Status = ToolIssueStatus.Canceled;
        issue.Note = request.Note ?? issue.Note;
        await RestoreToolStatusAsync(issue, ct);
        await db.SaveChangesAsync(ct);

        await auditLogger.LogAsync("Execution", "ToolAllocateCancel", nameof(ToolIssue), id.ToString(),
            detail: $"tool={issue.Tool!.Code} reason={request.Note}", ct: ct);
        return Outcome<ToolIssue>.Ok(issue);
    }

    /// <summary>
    /// 治工具の状態を引当前へ戻す。メンテナンス中・廃棄へ変わっている場合はそのまま
    /// （返却が状態を上書きして、メンテ中の治工具を使える状態に戻してしまわないようにする）
    /// </summary>
    private async Task RestoreToolStatusAsync(ToolIssue issue, CancellationToken ct)
    {
        var tool = issue.Tool ?? await db.Tools.FindAsync([issue.ToolId], ct);
        if (tool is not null && tool.Status == ToolStatus.InUse)
        {
            tool.Status = ToolStatus.Available;
        }
    }

    /// <summary>引当・払出の可否（寿命の累計と他の引当を見る）</summary>
    private async Task<string?> CheckIssuableAsync(Tool tool, CancellationToken ct, int? ignoreIssueId = null)
    {
        // 寿命の累計はリセット（メンテ完了）以降だけを数える。E-60-20-02 と同じ条件
        var usages = await db.ToolUsages.AsNoTracking()
            .Where(u => u.ToolId == tool.Id)
            .Select(u => new { u.UsageCount, u.UsageHours, u.RecordedAt })
            .ToListAsync(ct);
        var effective = usages
            .Where(u => tool.LifeResetAt is null || u.RecordedAt > tool.LifeResetAt)
            .ToList();
        var lifeReached = ToolIssuePolicy.IsLifeReached(
            tool, effective.Sum(u => u.UsageCount), effective.Sum(u => u.UsageHours ?? 0));

        var hasOpen = await db.ToolIssues.AsNoTracking()
            .AnyAsync(i => i.ToolId == tool.Id
                           && (ignoreIssueId == null || i.Id != ignoreIssueId)
                           && (i.Status == ToolIssueStatus.Allocated || i.Status == ToolIssueStatus.Issued), ct);

        return ToolIssuePolicy.CheckIssuable(tool, lifeReached, hasOpen);
    }
}
