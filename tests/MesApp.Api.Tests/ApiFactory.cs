using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MesApp.Api.Tests;

/// <summary>
/// テストごとに独立したSQLite一時DBを使うAPIファクトリ
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _workDir =
        Directory.CreateTempSubdirectory("mesapp-test-").FullName;

    private readonly IReadOnlyDictionary<string, string> _settings;

    public ApiFactory() : this(new Dictionary<string, string>())
    {
    }

    /// <summary>起動時の設定を足したいテスト用（初期管理者シードの条件など）</summary>
    public ApiFactory(IReadOnlyDictionary<string, string> settings) => _settings = settings;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", $"Data Source={Path.Combine(_workDir, "test.db")}");
        builder.UseSetting("Jwt:SigningKeyFile", Path.Combine(_workDir, "jwt-signing.key"));
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_workDir, recursive: true);
        }
        catch (IOException)
        {
            // SQLiteのファイルハンドル解放が遅れることがあるため、削除失敗は無視（一時ディレクトリはOSが回収）
        }
    }
}
