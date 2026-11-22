namespace MesApp.Core.Entities;

/// <summary>
/// 連携API の再送への備え（<c>Idempotency-Key</c>。Spec.md 3.8 連携API。docs-dev/IntegrationApiTasks.md INT-02）。
/// <para>
/// 同じ呼び出し元が同じキーで送り直したら、取り込み直さずに前の応答を返す。実績CSVは常に新規登録なので、
/// 通信が切れて送り直すと二重に登録されるため。<b>取り込む前に「処理中」の行を入れ</b>（一意索引で同時の重複を止める）、
/// 終わったら応答を書き込む。保持は24時間で、過ぎたものは使われたときに消す。
/// </para>
/// </summary>
public class IntegrationRequest
{
    public long Id { get; set; }

    /// <summary>呼び出し元（APIキーなら <c>key:ID</c>、ログインなら <c>user:利用者ID</c>）。キーは呼び出し元ごとに別</summary>
    public string Caller { get; set; } = string.Empty;

    /// <summary>呼び出し元が付けた <c>Idempotency-Key</c></summary>
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>要求（メソッド・パス・クエリ・本文）の SHA-256。同じキーで中身が違う要求を見分ける</summary>
    public string RequestHash { get; set; } = string.Empty;

    /// <summary>応答の状態コード。0 は処理中</summary>
    public int StatusCode { get; set; }

    /// <summary>応答の本文（JSON）。処理中は null</summary>
    public string? ResponseBody { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 有効期限（Unix 秒）。期限の判定と掃除を DB 側で行うために整数で持つ
    /// （SQLite は DateTimeOffset を DB 側で比べられない）
    /// </summary>
    public long ExpiresAtUnixSeconds { get; set; }
}
