using MesApp.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MesApp.Api.Tests;

/// <summary>
/// 製造日（業務日付）の算出（Spec.md 3.9）。日次集計・日報・ロット番号の日付がここで決まるため、
/// サーバーのタイムゾーンに引きずられないことを確認する。
/// </summary>
public class BusinessDateTests
{
    private static BusinessDateService Create(string? timeZone, int boundaryHour = 6)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BusinessDay:BoundaryHour"] = boundaryHour.ToString(),
                ["BusinessDay:TimeZone"] = timeZone,
            })
            .Build();
        return new BusinessDateService(configuration, NullLogger<BusinessDateService>.Instance);
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
