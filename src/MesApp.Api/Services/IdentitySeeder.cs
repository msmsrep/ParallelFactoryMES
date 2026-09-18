using System.Security.Cryptography;
using MesApp.Core.Constants;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MesApp.Api.Services;

/// <summary>
/// 起動時シード：ロール作成と、無人セットアップ用の初期管理者シード（Spec.md 2.2 E）。
/// 初期管理者は Userが0件 かつ 設定（<c>MesAdmin:UserName</c>）がある場合のみ作成し、
/// 初回ログイン時のパスワード変更を強制する。
/// </summary>
/// <remarks>
/// パスワードの決め方は2通り。
/// <list type="bullet">
/// <item><c>MesAdmin:Password</c> を設定する（zip/Docker等の無人セットアップ。運用者が値を知っている前提）</item>
/// <item><c>MesAdmin:GeneratePassword=true</c> にする（デスクトップ配布。インストールごとに自動生成し、
/// 生成値は保存せず画面に表示する。変更が済むまでは起動のたびに作り直すため、控えは不要）</item>
/// </list>
/// どちらもない場合は初期管理者を作らない（誰も知らないパスワードのアカウントを残さないため）。
/// </remarks>
public static class IdentitySeeder
{
    /// <summary>自動生成パスワードの文字数（1文字あたり約5bit）</summary>
    private const int GeneratedPasswordLength = 16;

    /// <summary>画面から書き写す前提のため、見間違えやすい文字（l・I・O・0・1）を除く</summary>
    private const string PasswordLetters = "abcdefghjkmnpqrstuvwxyz";
    private const string PasswordDigits = "23456789";

    /// <summary>
    /// ロールを作成し、必要なら初期管理者をシードする。
    /// 初期管理者がまだパスワードを変更していない場合は、その資格情報を返す
    /// （デスクトップ配布では利用者が手元に手順書を持たないため、画面に案内する必要がある）。
    /// </summary>
    public static async Task<InitialCredentials?> SeedAsync(
        IServiceProvider serviceProvider, IConfiguration configuration)
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
        if (string.IsNullOrWhiteSpace(seedUserName))
        {
            return null;
        }

        var configuredPassword = configuration["MesAdmin:Password"];
        var generate = configuration.GetValue("MesAdmin:GeneratePassword", false);
        if (string.IsNullOrWhiteSpace(configuredPassword) && !generate)
        {
            // 値を誰も知らないアカウントを作ってしまわないよう、何もしない
            return null;
        }
        var regeneratesOnRestart = string.IsNullOrWhiteSpace(configuredPassword);

        if (!await userManager.Users.AnyAsync())
        {
            return await CreateAdminAsync(
                userManager, logger, configuration, seedUserName, configuredPassword, regeneratesOnRestart);
        }

        // 既にユーザーがいる場合は作らない。初期管理者がパスワード未変更なら案内だけ続ける
        var admin = await userManager.FindByNameAsync(seedUserName);
        if (admin is not { MustChangePassword: true })
        {
            return null;
        }
        if (!regeneratesOnRestart)
        {
            return new InitialCredentials(seedUserName, configuredPassword!, RegeneratesOnRestart: false);
        }

        // 自動生成したパスワードは保存していない（ハッシュのみ）。
        // 案内を続けるには値が要るので、変更が済むまでは起動のたびに作り直す
        return await ResetToGeneratedPasswordAsync(userManager, logger, admin, seedUserName);
    }

    /// <summary>初期管理者がまだパスワードを変更していないか（デスクトップの案内表示の要否判定）</summary>
    public static async Task<bool> IsInitialPasswordPendingAsync(
        IServiceProvider serviceProvider, IConfiguration configuration)
    {
        var seedUserName = configuration["MesAdmin:UserName"];
        if (string.IsNullOrWhiteSpace(seedUserName))
        {
            return false;
        }

        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return await userManager.FindByNameAsync(seedUserName) is { MustChangePassword: true };
    }

    private static async Task<InitialCredentials?> CreateAdminAsync(
        UserManager<AppUser> userManager, ILogger logger, IConfiguration configuration,
        string seedUserName, string? configuredPassword, bool regeneratesOnRestart)
    {
        var password = regeneratesOnRestart ? GeneratePassword() : configuredPassword!;
        var admin = new AppUser
        {
            UserName = seedUserName,
            DisplayName = configuration["MesAdmin:DisplayName"] ?? "初期管理者",
            IsActive = true,
            MustChangePassword = true,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            logger.LogError("初期管理者アカウントのシードに失敗: {Errors}",
                string.Join("; ", result.Errors.Select(e => e.Description)));
            return null;
        }

        await userManager.AddToRoleAsync(admin, MesRoles.SystemAdmin);
        logger.LogInformation(
            "初期管理者アカウント '{UserName}' をシードしました（初回ログイン時にパスワード変更が必要）。", seedUserName);
        return new InitialCredentials(seedUserName, password, regeneratesOnRestart);
    }

    private static async Task<InitialCredentials?> ResetToGeneratedPasswordAsync(
        UserManager<AppUser> userManager, ILogger logger, AppUser admin, string seedUserName)
    {
        var password = GeneratePassword();
        var token = await userManager.GeneratePasswordResetTokenAsync(admin);
        var reset = await userManager.ResetPasswordAsync(admin, token, password);
        if (!reset.Succeeded)
        {
            logger.LogError("初期管理者の初期パスワード再生成に失敗: {Errors}",
                string.Join("; ", reset.Errors.Select(e => e.Description)));
            return null;
        }

        logger.LogInformation(
            "初期管理者 '{UserName}' の初期パスワードを再生成しました（値は画面に表示されます）。", seedUserName);
        return new InitialCredentials(seedUserName, password, RegeneratesOnRestart: true);
    }

    /// <summary>
    /// 初期パスワードの生成。パスワードポリシー（8文字以上・英小文字・数字を含む）を必ず満たすよう、
    /// 英字と数字を1文字ずつ確定で混ぜる。
    /// </summary>
    private static string GeneratePassword()
    {
        const string alphabet = PasswordLetters + PasswordDigits;
        var chars = new char[GeneratedPasswordLength];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        chars[0] = PasswordLetters[RandomNumberGenerator.GetInt32(PasswordLetters.Length)];
        chars[^1] = PasswordDigits[RandomNumberGenerator.GetInt32(PasswordDigits.Length)];
        return new string(chars);
    }
}

/// <summary>初回ログイン前の初期管理者の資格情報</summary>
/// <param name="RegeneratesOnRestart">
/// 自動生成のため、パスワードを変更するまでは起動のたびに新しくなる（控えても次回は使えない）
/// </param>
public sealed record InitialCredentials(string UserName, string Password, bool RegeneratesOnRestart);
