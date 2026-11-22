using System.Security.Cryptography;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary><see cref="IdempotencyService.StartAsync"/> の結果</summary>
public abstract record IdempotencyStart
{
    /// <summary>初めての要求。処理してから <see cref="IdempotencyService.CompleteAsync"/> で応答を残す</summary>
    public sealed record Proceed(long Id) : IdempotencyStart;

    /// <summary>同じ要求の送り直し。前の応答をそのまま返す</summary>
    public sealed record Replay(int StatusCode, string Body) : IdempotencyStart;

    /// <summary>同じキーで処理中か、同じキーで中身の違う要求（409）</summary>
    public sealed record Conflict(bool InProgress) : IdempotencyStart;
}

/// <summary>
/// 連携API の再送への備え（<c>Idempotency-Key</c>。Spec.md 3.8 連携API。<see cref="IntegrationRequest"/>）。
/// <b>取り込む前に処理中の行を入れ</b>、一意索引で同じキーの同時の要求を止める。処理が終われば応答を書き込み、
/// 例外で終われば行を消す（送り直せば最初から処理する）。保持は24時間
/// </summary>
public sealed class IdempotencyService(MesAppDbContext db)
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>キーの長さの上限（UUID・時刻＋連番などを想定）</summary>
    public const int MaxKeyLength = 100;

    /// <summary>応答を覚えておく時間。過ぎたら同じキーでも新しい要求として扱う</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    /// <summary>要求のハッシュ（メソッド・パス・クエリ・本文）。同じキーで中身の違う要求を見分ける</summary>
    public static string HashRequest(HttpRequest request, byte[] body)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(System.Text.Encoding.UTF8.GetBytes($"{request.Method} {request.Path}{request.QueryString}\n"));
        sha.AppendData(body);
        return Convert.ToBase64String(sha.GetHashAndReset());
    }

    public async Task<IdempotencyStart> StartAsync(string caller, string key, string requestHash, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // 期限切れの掃除は使われたついでに行う（常駐の掃除役を増やさない）
        await db.IntegrationRequests.Where(r => r.ExpiresAtUnixSeconds <= now).ExecuteDeleteAsync(ct);

        var existing = await db.IntegrationRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Caller == caller && r.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.StatusCode == 0)
            {
                return new IdempotencyStart.Conflict(InProgress: true);
            }
            return existing.RequestHash == requestHash
                ? new IdempotencyStart.Replay(existing.StatusCode, existing.ResponseBody ?? "")
                : new IdempotencyStart.Conflict(InProgress: false);
        }

        var row = new IntegrationRequest
        {
            Caller = caller,
            IdempotencyKey = key,
            RequestHash = requestHash,
            ExpiresAtUnixSeconds = now + (long)Retention.TotalSeconds,
        };
        db.IntegrationRequests.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // 同じキーの要求が同時に来て、向こうが先に行を入れた
            db.ChangeTracker.Clear();
            return new IdempotencyStart.Conflict(InProgress: true);
        }
        // 取込の処理（変更追跡を使う）に混ざらないよう、追跡から外しておく
        db.Entry(row).State = EntityState.Detached;
        return new IdempotencyStart.Proceed(row.Id);
    }

    /// <summary>処理の結果を残す（取込のトランザクションとは別に書く。取込が取り消されても応答は残す）</summary>
    public Task CompleteAsync(long id, int statusCode, string body, CancellationToken ct) =>
        db.IntegrationRequests.Where(r => r.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.StatusCode, statusCode)
            .SetProperty(r => r.ResponseBody, body), ct);

    /// <summary>処理が例外で終わったら行を消す（同じキーで送り直せば、最初から処理する）</summary>
    public Task AbandonAsync(long id) =>
        db.IntegrationRequests.Where(r => r.Id == id).ExecuteDeleteAsync(CancellationToken.None);
}
