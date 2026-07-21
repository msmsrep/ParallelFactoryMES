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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:ConnectionString", $"Data Source={Path.Combine(_workDir, "test.db")}");
        builder.UseSetting("Jwt:SigningKeyFile", Path.Combine(_workDir, "jwt-signing.key"));
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
