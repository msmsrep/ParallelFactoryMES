using System.Net.Http.Json;
using MesApp.Api.Services;
using MesApp.Core.Contracts.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesApp.Api.Tests;

/// <summary>
/// 製造日（業務日付）の算出（Spec.md 3.9）。日次集計・日報・ロット番号の日付がここで決まるため、
/// サーバーのタイムゾーンに引きずられないことを確認する。
/// </summary>
public class BusinessDateTests
{
    private static BusinessDateService Create(string? timeZone, int boundaryHour = 6) =>
        Create(timeZone, boundaryHour.ToString());

    private static BusinessDateService Create(string? timeZone, string? boundaryHour)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BusinessDay:BoundaryHour"] = boundaryHour,
                ["BusinessDay:TimeZone"] = timeZone,
            })
            .Build();
        return new BusinessDateService(configuration, NullLogger<BusinessDateService>.Instance);
    }

    [Fact]
    public async Task 製造日APIが当日と境界時刻を返す()
    {
        // 境界0時なら製造日は暦日と一致するので、実行時刻に依存せず期待値を組み立てられる
        using var factory = new ApiFactory(
            new Dictionary<string, string> { ["BusinessDay:BoundaryHour"] = "0" });
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var before = DateOnly.FromDateTime(DateTime.Now);
        var body = await admin.GetFromJsonAsync<BusinessDateResponse>("/api/business-date");
        var after = DateOnly.FromDateTime(DateTime.Now);

        Assert.Equal(0, body!.BoundaryHour);
        // 日付をまたぐ瞬間に実行された場合だけ、前後どちらの暦日も正しい
        Assert.True(body.Today == before || body.Today == after);
    }

    [Fact]
    public async Task 製造日APIの境界時刻は既定で6時()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var body = await admin.GetFromJsonAsync<BusinessDateResponse>("/api/business-date");

        Assert.Equal(6, body!.BoundaryHour);
    }

    [Fact]
    public void 工場のタイムゾーンを基準に境界時刻を判定する()
    {
        var service = Create("Asia/Tokyo");

        // UTC 20:00 = JST 翌05:00。境界6時より前なので製造日は前日のまま
        Assert.Equal(new DateOnly(2026, 9, 3),
            service.GetBusinessDate(new DateTimeOffset(2026, 9, 3, 20, 0, 0, TimeSpan.Zero)));

        // UTC 21:00 = JST 翌06:00。境界に達したので製造日が翌日になる
        Assert.Equal(new DateOnly(2026, 9, 4),
            service.GetBusinessDate(new DateTimeOffset(2026, 9, 3, 21, 0, 0, TimeSpan.Zero)));

        // 入力のオフセットが何であっても結果は変わらない（同じ瞬間なら同じ製造日）
        Assert.Equal(new DateOnly(2026, 9, 3),
            service.GetBusinessDate(new DateTimeOffset(2026, 9, 4, 5, 0, 0, TimeSpan.FromHours(9))));
    }

    [Fact]
    public void 製造日の範囲はその日のオフセットで返る()
    {
        var service = Create("Asia/Tokyo");

        var (start, end) = service.GetRange(new DateOnly(2026, 9, 3));

        Assert.Equal(TimeSpan.FromHours(9), start.Offset);
        Assert.Equal(new DateTime(2026, 9, 3, 6, 0, 0), start.DateTime);
        Assert.Equal(new DateTime(2026, 9, 4, 6, 0, 0), end.DateTime);
        Assert.Equal(TimeSpan.FromHours(24), end - start);
    }

    [Fact]
    public void 夏時間をまたぐ日でも1日の長さが実時間に合う()
    {
        // 米国東部は2026-03-08に夏時間へ入る（現地02:00→03:00）。この日は実時間23時間
        var service = Create("America/New_York");

        var (start, end) = service.GetRange(new DateOnly(2026, 3, 7));

        Assert.Equal(TimeSpan.FromHours(23), end - start);
    }

    [Fact]
    public void 未設定や解決できない指定はサーバーのローカルタイムに落ちる()
    {
        var expected = new BusinessDateServiceProbe(TimeZoneInfo.Local);

        foreach (var service in new[] { Create(null), Create("Nowhere/Standard Time") })
        {
            var moment = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
            Assert.Equal(expected.BusinessDate(moment), service.GetBusinessDate(moment));
        }
    }

    [Theory]
    [InlineData("24")]   // 0〜23の範囲外
    [InlineData("-1")]
    [InlineData("6.5")]  // 整数でない
    [InlineData("朝6時")] // 数値でない
    [InlineData("")]     // 空（未設定と同じ扱い）
    public void 境界時刻の不正な設定は既定の6時に落ちる(string boundaryHour)
    {
        // シングルトンの生成は遅延なので、ここで例外を投げると「起動は成功して
        // 最初のリクエストで全画面500」になる。原因にたどり着けないため落として使う
        var service = Create("Asia/Tokyo", boundaryHour);

        Assert.Equal(6, service.BoundaryHour);
        // 範囲の算出も例外にならない（TimeOnly/DateTime の生成に不正な時刻が渡らない）
        var (start, end) = service.GetRange(new DateOnly(2026, 9, 3));
        Assert.Equal(new DateTime(2026, 9, 3, 6, 0, 0), start.DateTime);
        Assert.Equal(TimeSpan.FromHours(24), end - start);
    }

    [Fact]
    public async Task 境界時刻が不正でも起動して製造日APIが既定値を返す()
    {
        using var factory = new ApiFactory(
            new Dictionary<string, string> { ["BusinessDay:BoundaryHour"] = "24" });
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var body = await admin.GetFromJsonAsync<BusinessDateResponse>("/api/business-date");

        Assert.Equal(6, body!.BoundaryHour);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public void 境界時刻は0時と23時を受け付ける(int boundaryHour)
    {
        var service = Create("Asia/Tokyo", boundaryHour);

        Assert.Equal(boundaryHour, service.BoundaryHour);
        Assert.Equal(new TimeSpan(boundaryHour, 0, 0),
            service.GetRange(new DateOnly(2026, 9, 3)).Start.TimeOfDay);
    }

    /// <summary>期待値の計算（テスト側でタイムゾーン変換を再現する）</summary>
    private sealed class BusinessDateServiceProbe(TimeZoneInfo timeZone)
    {
        public DateOnly BusinessDate(DateTimeOffset moment)
        {
            var local = TimeZoneInfo.ConvertTime(moment, timeZone);
            var date = DateOnly.FromDateTime(local.DateTime);
            return local.Hour < 6 ? date.AddDays(-1) : date;
        }
    }
}
