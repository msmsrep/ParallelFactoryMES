using MesApp.Api.Services;
using MesApp.Core.Abstractions;
using MesApp.Core.Entities;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Blazor WASMクライアントの静的配信（Spec.md 2.1：Webクライアントの静的配信もAPIが担う）。
// 非Development環境（テスト・zip配布のdotnet run等）でも静的Webアセットを解決できるようにする
if (!builder.Environment.IsProduction())
{
    builder.WebHost.UseStaticWebAssets();
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

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
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var keyProvider = new SigningKeyProvider(Microsoft.Extensions.Options.Options.Create(jwt));
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
// クライアント側ルーティングのフォールバック（/manufacturing-orders 等の直接アクセス）
app.MapFallbackToFile("index.html");

// DB初期化（マイグレーション適用・SQLite WAL）とロール・初期管理者シード
await app.Services.InitializeDatabaseAsync();
await IdentitySeeder.SeedAsync(app.Services, app.Configuration);

app.Run();

/// <summary>統合テスト（WebApplicationFactory）用</summary>
public partial class Program;
