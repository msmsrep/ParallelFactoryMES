namespace MesApp.Infrastructure;

/// <summary>
/// DBプロバイダー設定（Spec.md 4章：appsettings.jsonの"Database"セクション）
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>"Sqlite" | "PostgreSql" | "SqlServer"（初期実装はSqliteのみ。他はPhase 10で対応）</summary>
    public string Provider { get; set; } = "Sqlite";

    public string ConnectionString { get; set; } = "Data Source=mesapp.db";
}
