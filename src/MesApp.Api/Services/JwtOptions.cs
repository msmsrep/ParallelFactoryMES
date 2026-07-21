namespace MesApp.Api.Services;

/// <summary>
/// JWT設定（Spec.md 7.4：アクセストークン既定60分、リフレッシュトークン既定12時間（交代勤務のシフト長考慮））
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "MesApp";
    public string Audience { get; set; } = "MesApp";
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
    public int RefreshTokenLifetimeHours { get; set; } = 12;

    /// <summary>署名鍵（Base64）。未設定時はSigningKeyFileから読み込み／自動生成する</summary>
    public string? SigningKey { get; set; }

    /// <summary>署名鍵の永続化ファイルパス（zip/Docker配布での無人セットアップ用）</summary>
    public string SigningKeyFile { get; set; } = "jwt-signing.key";
}
