using MesApp.Core.Abstractions;

namespace MesApp.Api.Services;

/// <summary>
/// 製造日（業務日付）の算出。境界時刻（既定6時）より前は前日の製造日として扱う（Spec.md 3.9）
/// </summary>
/// <remarks>
/// 基準は工場のタイムゾーン（<c>BusinessDay:TimeZone</c>）。未設定ならサーバーのローカルタイムを使う。
/// サーバー運用でタイムゾーンがUTCのコンテナに置くと、設定しない限り境界時刻が実際の現場と
/// ずれる（日本なら9時間）。日次集計・日報・ロット番号の日付がまとめてずれるため、
/// 設定で明示できるようにしている。
/// </remarks>
public class BusinessDateService : IBusinessDateService
{
    private readonly int _boundaryHour;
    private readonly TimeZoneInfo _timeZone;

    public BusinessDateService(IConfiguration configuration, ILogger<BusinessDateService> logger)
    {
        _boundaryHour = configuration.GetValue("BusinessDay:BoundaryHour", 6);
        _timeZone = ResolveTimeZone(configuration.GetValue<string?>("BusinessDay:TimeZone"), logger);
    }

    public DateOnly GetBusinessDate(DateTimeOffset moment)
    {
        var local = TimeZoneInfo.ConvertTime(moment, _timeZone);
        var date = DateOnly.FromDateTime(local.DateTime);
        return local.Hour < _boundaryHour ? date.AddDays(-1) : date;
    }

    public DateOnly Today => GetBusinessDate(DateTimeOffset.UtcNow);

    public int BoundaryHour => _boundaryHour;

    public DateTimeOffset ToFactoryTime(DateTimeOffset moment) =>
        TimeZoneInfo.ConvertTime(moment, _timeZone);

    public (DateTimeOffset Start, DateTimeOffset End) GetRange(DateOnly businessDate) =>
        (StartOf(businessDate), StartOf(businessDate.AddDays(1)));

    /// <summary>
    /// 製造日の開始時刻。オフセットはその日のものを取る
    /// （「現在の」オフセットを使い回すと、夏時間のある地域で日付をまたぐ集計が1時間ずれる）。
    /// </summary>
    private DateTimeOffset StartOf(DateOnly businessDate)
    {
        var local = new DateTime(
            businessDate.Year, businessDate.Month, businessDate.Day,
            _boundaryHour, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _timeZone.GetUtcOffset(local));
    }

    /// <summary>
    /// 設定されたタイムゾーンを解決する。IANA名（Asia/Tokyo）とWindows名（Tokyo Standard Time）の
    /// どちらでも指定できる。見つからない場合はサーバーのローカルタイムへ落として警告を出す
    /// （起動を止めるとタイムゾーン名の綴り違いだけでシステムが上がらなくなるため）。
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string? id, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return TimeZoneInfo.Local;
        }
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(ex,
                "業務日付のタイムゾーン '{TimeZone}' を解決できません。サーバーのローカルタイム（{Local}）を使います",
                id, TimeZoneInfo.Local.Id);
            return TimeZoneInfo.Local;
        }
    }
}
