using MesApp.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MesApp.Api.Tests;

/// <summary>
/// テストごとに独立したDBを使うAPIファクトリ。既定はSQLiteの一時ファイル。
/// </summary>
/// <remarks>
/// 環境変数 <c>MESAPP_TEST_PROVIDER</c>（<c>PostgreSql</c> / <c>SqlServer</c>）と <c>MESAPP_TEST_CONNECTION</c>
/// （DB作成権限のあるログインの接続文字列。DB名は無視する）を設定すると、同じテストを実DBで流せる（Spec.md 4章）。
/// ファクトリごとに一意な名前のDBを作り、破棄時に消す。
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string ProviderVariable = "MESAPP_TEST_PROVIDER";
    public const string ConnectionVariable = "MESAPP_TEST_CONNECTION";

    private readonly string _workDir =
        Directory.CreateTempSubdirectory("mesapp-test-").FullName;

    private readonly IReadOnlyDictionary<string, string> _settings;
    private readonly DatabaseOptions _database;

    public ApiFactory() : this(new Dictionary<string, string>())
    {
    }

    /// <summary>起動時の設定を足したいテスト用（初期管理者シードの条件など）</summary>
    public ApiFactory(IReadOnlyDictionary<string, string> settings)
    {
        _settings = settings;
        _database = CreateDatabaseOptions();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", _database.Provider);
        builder.UseSetting("Database:ConnectionString", _database.ConnectionString);
        builder.UseSetting("Jwt:SigningKeyFile", Path.Combine(_workDir, "jwt-signing.key"));
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>環境変数が無ければ一時ディレクトリのSQLite</summary>
    private DatabaseOptions CreateDatabaseOptions()
    {
        var provider = Environment.GetEnvironmentVariable(ProviderVariable);
        if (string.IsNullOrWhiteSpace(provider) || provider == DatabaseProviders.Sqlite)
        {
            return new DatabaseOptions { Provider = DatabaseProviders.Sqlite, ConnectionString = $"Data Source={Path.Combine(_workDir, "test.db")}" };
        }

        var connection = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                $"{ProviderVariable}={provider} のときは {ConnectionVariable} に接続文字列を設定してください。");
        }

        // 並列に走るテストが互いのデータを見ないよう、ファクトリごとにDBを分ける
        var database = $"mesapp_test_{Guid.NewGuid():N}";
        var connectionString = provider switch
        {
            DatabaseProviders.PostgreSql => new NpgsqlConnectionStringBuilder(connection) { Database = database }.ToString(),
            DatabaseProviders.SqlServer => new SqlConnectionStringBuilder(connection) { InitialCatalog = database }.ToString(),
            _ => throw new InvalidOperationException($"{ProviderVariable} に不明なプロバイダー '{provider}' が設定されています。"),
        };
        return new DatabaseOptions { Provider = provider, ConnectionString = connectionString };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (_database.Provider != DatabaseProviders.Sqlite)
        {
            DropDatabase();
        }
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
            // SQLiteのファイルハンドル解放が遅れることがあるため、削除失敗は無視（一時ディレクトリはOSが回収）
        }
    }

    /// <summary>テスト用に作ったDBを消す。プールに残った接続が削除を妨げるので先に捨てる</summary>
    private void DropDatabase()
    {
        NpgsqlConnection.ClearAllPools();
        SqlConnection.ClearAllPools();
        var builder = new DbContextOptionsBuilder<MesAppDbContext>();
        DependencyInjection.UseProvider(builder, _database);
        using var db = new MesAppDbContext(builder.Options);

        // PostgreSQLは大量に書き込んだ直後のDBで autovacuum が動いていることがあり、
        // テスト用ロールにはそれを止める権限が無いため削除が 42501 で失敗する。終わるのを待ってやり直す
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                db.Database.EnsureDeleted();
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.InsufficientPrivilege && attempt < 10)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }
    }
}
