namespace MesApp.Infrastructure;

/// <summary>
/// 設定値 <c>Database:Provider</c> に書ける名前（Spec.md 4章）
/// </summary>
public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";
    public const string PostgreSql = "PostgreSql";
    public const string SqlServer = "SqlServer";

    /// <summary>対応しているプロバイダーすべて（エラーメッセージ・テストの列挙に使う）</summary>
    public static readonly IReadOnlyList<string> All = [Sqlite, PostgreSql, SqlServer];
}
