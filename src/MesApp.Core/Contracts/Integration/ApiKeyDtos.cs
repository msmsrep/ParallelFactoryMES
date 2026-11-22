namespace MesApp.Core.Contracts.Integration;

/// <summary>APIキーの一覧の1行（Spec.md 7.4 連携APIの認証）。平文もハッシュも含めない</summary>
/// <param name="Prefix">キーの先頭（例 pfm_AbCd1234）。手元のキーと見分けるためだけに使う</param>
public record ApiKeyResponse(
    int Id,
    string Name,
    string UserId,
    string UserName,
    string UserDisplayName,
    string Prefix,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

/// <summary>APIキーの発行</summary>
/// <param name="Name">用途（例：販売管理からの指図取込）</param>
/// <param name="UserId">キーで呼んだときの利用者。権限はこの利用者のロールで判定する</param>
/// <param name="ExpiresAt">有効期限（任意）</param>
public record CreateApiKeyRequest(string Name, string UserId, DateTimeOffset? ExpiresAt);

/// <summary>発行の結果。<paramref name="PlainKey"/> はこの応答でしか返さない（再表示できない）</summary>
public record CreatedApiKeyResponse(ApiKeyResponse Key, string PlainKey);

/// <summary>連携API の疎通確認（誰として認証されたか）</summary>
/// <param name="AuthenticatedBy">ApiKey または Bearer（ログインのトークン）</param>
/// <param name="ApiKeyName">APIキーで認証したときのキーの名前</param>
public record IntegrationWhoAmIResponse(
    string UserName,
    string DisplayName,
    List<string> Roles,
    string AuthenticatedBy,
    string? ApiKeyName);
