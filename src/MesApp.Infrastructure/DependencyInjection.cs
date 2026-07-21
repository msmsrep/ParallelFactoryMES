using MesApp.Core.Abstractions;
using MesApp.Infrastructure.Services;
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
                    db.UseSqlite(options.ConnectionString, sqlite =>
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
        }
    }
}
