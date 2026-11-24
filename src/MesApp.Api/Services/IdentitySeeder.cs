using MesApp.Core.Constants;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 起動時シード：ロール作成と、無人セットアップ用の初期管理者シード（Spec.md 2.2 E）。
/// 初期管理者は Userが0件 かつ 設定（<c>MesAdmin:UserName</c>・<c>MesAdmin:Password</c>）がある場合のみ作成し、
/// 初回ログイン時のパスワード変更を強制する。
/// </summary>
/// <remarks>
/// パスワードは運用者が設定で渡す（zip/Docker等の無人セットアップ。運用者が値を知っている前提）。
/// 指定がない場合は初期管理者を作らず、初期セットアップ画面（<c>/setup</c>）で利用者が作る
/// （デスクトップ版もこちら。誰も知らないパスワードのアカウントを残さないため）。
/// </remarks>
public static class IdentitySeeder
{
    /// <summary>ロールを作成し、設定があれば初期管理者をシードする</summary>
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

        var seedUserName = configuration["MesAdmin:UserName"];
        var seedPassword = configuration["MesAdmin:Password"];
        if (string.IsNullOrWhiteSpace(seedUserName) || string.IsNullOrWhiteSpace(seedPassword))
        {
            // 値を誰も知らないアカウントを作ってしまわないよう、何もしない
            return;
        }

        if (await userManager.Users.AnyAsync())
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
        if (!result.Succeeded)
        {
            logger.LogError("初期管理者アカウントのシードに失敗: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }

        await userManager.AddToRoleAsync(admin, MesRoles.SystemAdmin);
        logger.LogInformation(
            "初期管理者アカウント '{UserName}' をシードしました（初回ログイン時にパスワード変更が必要）。", seedUserName);
    }
}
