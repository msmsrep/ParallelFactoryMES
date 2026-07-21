using MesApp.Core.Abstractions;

namespace MesApp.Api.Services;

/// <summary>
/// 製造日（業務日付）の算出。境界時刻（既定6時）より前は前日の製造日として扱う（Spec.md 3.9）
/// </summary>
public class BusinessDateService(IConfiguration configuration) : IBusinessDateService
{
    private readonly int _boundaryHour = configuration.GetValue("BusinessDay:BoundaryHour", 6);

    public DateOnly GetBusinessDate(DateTimeOffset moment)
    {
        var local = moment.ToLocalTime();
        var date = DateOnly.FromDateTime(local.DateTime);
        return local.Hour < _boundaryHour ? date.AddDays(-1) : date;
    }

    public DateOnly Today => GetBusinessDate(DateTimeOffset.Now);

    public (DateTimeOffset Start, DateTimeOffset End) GetRange(DateOnly businessDate)
    {
        var start = new DateTimeOffset(
            businessDate.Year, businessDate.Month, businessDate.Day,
            _boundaryHour, 0, 0, DateTimeOffset.Now.Offset);
        return (start, start.AddDays(1));
    }
}
