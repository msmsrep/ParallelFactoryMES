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
