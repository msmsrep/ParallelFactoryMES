using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MesApp.Api;

/// <summary>
/// Webアプリの組み立てと初期化。Program.cs（サーバー実行）とMesApp.Desktop（単独PC向けMSIX配布）の
/// 双方が同じ起点を使うために切り出している。
/// </summary>
public static class MesAppHost
{
    /// <summary>サービス登録とミドルウェアパイプラインを構成したWebApplicationを返す</summary>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Blazor WASMクライアントの静的配信（Spec.md 2.1：Webクライアントの静的配信もAPIが担う）。
        // 非Development環境（テスト・zip配布のdotnet run等）でも静的Webアセットを解決できるようにする
        if (!builder.Environment.IsProduction())
        {
            builder.WebHost.UseStaticWebAssets();
        }

        // MesApp.Desktopから起動するとエントリアセンブリがAPIではなくなり、コントローラの自動探索が働かない。
        // このアセンブリを明示登録する（重複登録するとルートが二重になるため存在チェックを挟む）。
        builder.Services.AddControllers(options =>
            {
                options.Filters.Add<MustChangePasswordFilter>();
                options.Filters.Add<MesAppExceptionFilter>();
            })
            .ConfigureApplicationPartManager(manager =>
            {
                var apiAssembly = typeof(MesAppHost).Assembly;
                if (!manager.ApplicationParts.OfType<AssemblyPart>().Any(part => part.Assembly == apiAssembly))
                {
                    manager.ApplicationParts.Add(new AssemblyPart(apiAssembly));
                }
            });
        builder.Services.AddOpenApi();
        builder.Services.AddHttpContextAccessor();
        // MesAppExceptionFilterで拾わない想定外の例外も、本文なしの500ではなくProblemDetailsで返す
        builder.Services.AddProblemDetails();

        // DB・監査ログ（Spec.md 4章・7.6）
        builder.Services.AddMesAppInfrastructure(builder.Configuration);

        // ASP.NET Core Identity（Spec.md 7.4）
        builder.Services
            .AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@";
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<MesAppDbContext>()
            .AddSignInManager();

        // JWT認証（アクセストークン。リフレッシュはRefreshTokenService＋HttpOnly Cookie）
        builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
        builder.Services.AddSingleton<SigningKeyProvider>();
        builder.Services.AddScoped<JwtTokenService>();
        builder.Services.AddScoped<RefreshTokenService>();
        builder.Services.AddSingleton<IBusinessDateService, BusinessDateService>();
        builder.Services.AddScoped<NumberingService>();
        builder.Services.AddScoped<InventoryService>();
        builder.Services.AddScoped<LotStatusService>();
        builder.Services.AddScoped<WorkOrderStatusService>();
        builder.Services.AddScoped<MasterCsvService>();

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // 検証鍵はDIのSigningKeyProvider（シングルトン）から取る。
        // ここで別インスタンスを作ると、鍵ファイルが無い初回に発行側と検証側で別の鍵が生まれる
        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyProvider, IOptions<JwtOptions>>((options, keyProvider, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = keyProvider.Key,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
            });

        builder.Services.AddAuthorization();

        var app = builder.Build();

        // 想定外の例外のフォールバック（開発環境では先に開発者例外ページが処理する）
        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        // Blazor WASMクライアントの配信（index.html・_framework・css）
        app.UseBlazorFrameworkFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        // クライアント側ルーティングのフォールバック（/manufacturing-orders 等の直接アクセス）。
        // api/ 配下で未マッチのものは404にする。除外しないとフォールバックが拾い、
        // 打ち間違い・未実装のAPIパスがindex.htmlの200になって、
        // 呼び出し側は404ではなくJSONパース失敗という無関係なエラーを受け取る
        app.MapFallback("api/{**rest}", () => Results.Problem(
            title: "指定されたAPIは存在しません。", statusCode: StatusCodes.Status404NotFound));
        app.MapFallbackToFile("index.html");

        return app;
    }

    /// <summary>
    /// DB初期化（マイグレーション適用・SQLite WAL）とロール・初期管理者シード。
    /// 初期管理者がまだパスワードを変更していない場合はその資格情報を返す（デスクトップ配布で画面に案内するため）。
    /// </summary>
    public static async Task<InitialCredentials?> InitializeAsync(WebApplication app)
    {
        await app.Services.InitializeDatabaseAsync();
        return await IdentitySeeder.SeedAsync(app.Services, app.Configuration);
    }
}
