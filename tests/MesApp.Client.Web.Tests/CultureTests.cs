using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using MesApp.Client.Web.Auth;
using MesApp.Client.Web.Layout;
using MesApp.Client.Web.Shared;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Auth;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Entities;
using MesApp.Core.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace MesApp.Client.Web.Tests;

/// <summary>
/// 表示言語の切替（Spec.md 7.9）。全画面の文言が UICulture と <see cref="UiText"/> の訳に従うため横断的に確認する。
/// </summary>
public class CultureTests : BunitContext
{
    public CultureTests()
    {
        Services.AddLocalization();

        var tokenStore = new TokenStore();
        Services.AddSingleton(tokenStore);
        var stateProvider = new ApiAuthenticationStateProvider(tokenStore);
        Services.AddSingleton(stateProvider);
        Services.AddSingleton(new AuthService(
            new HttpClient { BaseAddress = new Uri("http://localhost/") }, tokenStore, stateProvider));
        tokenStore.Set("token", new UserInfo("id-1", "worker1", "作業者1", [MesRoles.Operator], false));
        AddAuthorization().SetAuthorized("作業者1");

        JSInterop.SetupVoid("mesApp.culture.set", _ => true).SetVoidResult();
    }

    [Theory]
    [InlineData(null, "ja")]
    [InlineData("", "ja")]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("EN", "en")]
    [InlineData("ja-JP", "ja")]
    [InlineData("fr", "ja")]
    public void 対応していない言語は既定の日本語に寄せる(string? stored, string expected)
    {
        Assert.Equal(expected, MesCultures.Resolve(stored));
    }

    [Fact]
    public void 英語を選んでいると画面の文言が英語になる()
    {
        var component = RenderLayoutIn("en");

        Assert.Contains("Log out", component.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ログアウト", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void 日本語では原文のまま出る()
    {
        var component = RenderLayoutIn("ja");

        Assert.Contains("ログアウト", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void 英語ではレイアウトとメニューに日本語が残らない()
    {
        // 利用者名・本文（テストが渡す値）と、言語の選択肢（その言語自身の表記で出す）は訳さない
        var markup = RenderLayoutIn("en").Markup
            .Replace("作業者1", "", StringComparison.Ordinal)
            .Replace("本文", "", StringComparison.Ordinal)
            .Replace("日本語", "", StringComparison.Ordinal);

        Assert.Contains("Master Data", markup, StringComparison.Ordinal);
        Assert.DoesNotMatch(Japanese, markup);
    }

    [Fact]
    public void 共通部品の文言も英語になる()
    {
        var pager = InCulture("en", () => Render<Pager<string>>(p => p
            .Add(x => x.Result, new PagedResult<string>(["a", "b"], 5, 2, 2))
            .Add(x => x.OnPageChanged, _ => { })));

        Assert.Contains("3–4 of 5 (page 2 / 3)", pager.Markup, StringComparison.Ordinal);
        Assert.Contains("Next", pager.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void 言語を切り替えると保存して再読込する()
    {
        var component = RenderLayoutIn("ja");
        var navigation = Services.GetRequiredService<BunitNavigationManager>();

        component.Find("select").Change(MesCultures.English);

        Assert.Equal(MesCultures.English, JSInterop.VerifyInvoke("mesApp.culture.set").Arguments.Single());
        // 訳のサテライトアセンブリは起動時に読まれるため、同じ画面を強制再読込して反映する
        Assert.True(navigation.History.Single().Options.ForceLoad);
    }

    /// <summary>Core に定義された全区分値（新しい区分・値を足したときに表示名の書き忘れを検出する）</summary>
    public static TheoryData<Enum> AllEnumValues()
    {
        var data = new TheoryData<Enum>();
        foreach (var type in typeof(LotStockStatus).Assembly.GetTypes().Where(t => t.IsEnum && t.IsPublic))
        {
            foreach (Enum value in Enum.GetValues(type))
            {
                data.Add(value);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllEnumValues))]
    public void 全区分値に日本語の表示名と英語の訳がある(Enum value)
    {
        Assert.NotEqual(value.ToString(), EnumLabels.OriginalOf(value));

        var english = InCulture("en", () => EnumLabels.Of(value));
        Assert.DoesNotMatch(@"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]", english);
    }

    [Fact]
    public void 区分値とロールの表示名は表示言語に従う()
    {
        Assert.Equal("保留", InCulture("ja", () => EnumLabels.Of(LotStockStatus.OnHold)));
        Assert.Equal("On hold", InCulture("en", () => EnumLabels.Of(LotStockStatus.OnHold)));
        Assert.Equal("Quality assurance", InCulture("en", () => EnumLabels.Role(MesRoles.QualityAssurance)));
        // 訳さない用途向けの原文は言語に左右されない
        Assert.Equal("保留", InCulture("en", () => EnumLabels.OriginalOf(LotStockStatus.OnHold)));
    }

    private const string Japanese = @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]";

    private static T InCulture<T>(string culture, Func<T> action)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    private IRenderedComponent<MainLayout> RenderLayoutIn(string culture)
    {
        var original = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        try
        {
            return Render<MainLayout>(p => p.Add(l => l.Body, (RenderFragment)(b => b.AddContent(0, "本文"))));
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
