namespace MesApp.Infrastructure;

/// <summary>
/// DBプロバイダー設定（Spec.md 4章：appsettings.jsonの"Database"セクション）
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary><see cref="DatabaseProviders"/> のいずれか</summary>
    public string Provider { get; set; } = DatabaseProviders.Sqlite;

    public string ConnectionString { get; set; } = "Data Source=mesapp.db";
}

/// <summary>
/// 設定値 <c>Database:Provider</c> に書ける名前と、プロバイダーごとのマイグレーションの置き場（Spec.md 4章）
/// </summary>
/// <remarks>
/// マイグレーションはプロバイダーごとに別物になる（EF Coreの制約）。SQLiteはこのプロジェクトの
/// <c>Migrations/</c>、PostgreSQL・SQL Serverはそれぞれ専用のプロジェクトに置く。
/// </remarks>
public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";
    public const string PostgreSql = "PostgreSql";
    public const string SqlServer = "SqlServer";

    public const string PostgreSqlMigrationsAssembly = "MesApp.Migrations.PostgreSql";
    public const string SqlServerMigrationsAssembly = "MesApp.Migrations.SqlServer";
}
