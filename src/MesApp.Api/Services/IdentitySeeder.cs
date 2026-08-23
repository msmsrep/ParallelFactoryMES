using MesApp.Core.Constants;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 起動時シード：ロール作成と、無人セットアップ用の初期管理者シード（Spec.md 2.2 E）。
/// 初期管理者は Userが0件 かつ 設定（MesAdmin:UserName / MesAdmin:Password。環境変数 MesAdmin__UserName 等）がある場合のみ作成し、
/// 初回ログイン時のパスワード変更を強制する。
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        using var scope = serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

        foreach (var role in MesRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        if (await userManager.Users.AnyAsync())
        {
            return;
        }

        var seedUserName = configuration["MesAdmin:UserName"];
        var seedPassword = configuration["MesAdmin:Password"];
        if (string.IsNullOrWhiteSpace(seedUserName) || string.IsNullOrWhiteSpace(seedPassword))
        {
            return;
        }

        var admin = new AppUser
        {
            UserName = seedUserName,
            DisplayName = configuration["MesAdmin:DisplayName"] ?? "初期管理者",
            IsActive = true,
            MustChangePassword = true,
        };

        var result = await userManager.CreateAsync(admin, seedPassword);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, MesRoles.SystemAdmin);
            logger.LogInformation("初期管理者アカウント '{UserName}' をシードしました（初回ログイン時にパスワード変更が必要）。", seedUserName);
        }
        else
        {
            logger.LogError("初期管理者アカウントのシードに失敗: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// シードした初期管理者がまだ初回ログイン（パスワード変更）を済ませていない場合に、その資格情報を返す。
    /// デスクトップ配布（Spec.md 7.8）では利用者が手元に手順書を持たないため、画面に案内する必要がある。
    /// パスワード変更が済むと null になり、案内は自動的に消える。
    /// </summary>
    public static async Task<InitialCredentials?> GetPendingInitialCredentialsAsync(
        IServiceProvider serviceProvider, IConfiguration configuration)
    {
        var seedUserName = configuration["MesAdmin:UserName"];
        var seedPassword = configuration["MesAdmin:Password"];
        if (string.IsNullOrWhiteSpace(seedUserName) || string.IsNullOrWhiteSpace(seedPassword))
        {
            return null;
        }

        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var admin = await userManager.FindByNameAsync(seedUserName);
        return admin is { MustChangePassword: true }
            ? new InitialCredentials(seedUserName, seedPassword)
            : null;
    }
}

/// <summary>初回ログイン前の初期管理者の資格情報</summary>
public sealed record InitialCredentials(string UserName, string Password);
