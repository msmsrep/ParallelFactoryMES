using MesApp.Core.Abstractions;
using MesApp.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MesApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMesAppInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                      ?? new DatabaseOptions();

        services.AddDbContext<MesAppDbContext>(db =>
        {
            switch (options.Provider)
            {
                case "Sqlite":
                    db.UseSqlite(ResolveSqliteConnectionString(options.ConnectionString), sqlite =>
                        sqlite.CommandTimeout(30));
                    break;
                case "PostgreSql":
                case "SqlServer":
                    throw new NotSupportedException(
                        $"DBプロバイダー '{options.Provider}' はPhase 10で対応予定です。現時点では 'Sqlite' を指定してください。");
                default:
                    throw new InvalidOperationException(
                        $"不明なDBプロバイダー '{options.Provider}' が設定されています（Database:Provider）。");
            }
        });

        services.AddScoped<IAuditLogger, AuditLogger>();
        return services;
    }

    /// <summary>
    /// SQLite接続文字列のData Sourceを書き込み可能な絶対パスへ解決する。
    /// 相対パス（既定の"mesapp.db"）は実行ファイルの隣に作られてしまい、MSIX配布時に書き込めないため。
    /// </summary>
    private static string ResolveSqliteConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        // インメモリDB（:memory: / Mode=Memory）はファイルではないので触らない
        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || builder.Mode == SqliteOpenMode.Memory
            || builder.DataSource == ":memory:")
        {
            return connectionString;
        }

        builder.DataSource = MesAppDataDirectory.Resolve(builder.DataSource);
        return builder.ToString();
    }

    /// <summary>
    /// 起動時のDB初期化：マイグレーション適用と、SQLite利用時のWALモード有効化（Spec.md 4章）
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
        await db.Database.MigrateAsync();

        if (db.Database.IsSqlite())
        {
            // WALは一度設定するとDBファイルに永続化される。busy_timeoutは接続ごとのためEF側のCommandTimeoutと併用
            await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
            await BackfillAuditRecordedOnAsync(db);
        }
    }

    /// <summary>
    /// <c>AuditLog.RecordedOn</c>（期間絞り込み用の記録日）を、列の追加前からある行へ埋める。
    /// 埋めないと「先月の監査ログ」に古い行が出てこない／既定値のまま混ざる（Spec.md 7.6）。
    /// </summary>
    /// <remarks>
    /// <c>Timestamp</c> はオフセット付きISO形式のTEXTで保存されるため、SQLite側でローカル時刻へ
    /// 直してから日付を取る（記録日はローカル日付。<c>AuditLog.RecordedOn</c> 参照）。
    /// 既定値の行だけを対象にするので、2回目以降は索引で即座に0件になる。
    /// </remarks>
    private static async Task BackfillAuditRecordedOnAsync(MesAppDbContext db) =>
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE AuditLogs SET RecordedOn = date(Timestamp, 'localtime') WHERE RecordedOn = '0001-01-01'");
}
