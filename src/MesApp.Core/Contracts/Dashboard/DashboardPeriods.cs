namespace MesApp.Core.Contracts.Dashboard;

/// <summary>
/// ダッシュボードの期間の区切り方（週は月曜始まり、月は暦月）。
/// APIの集計と画面の期間選択で同じ区切りを使う（別々に書くと、画面が選んだ週とAPIが数える週がずれる）
/// </summary>
public static class DashboardPeriods
{
    /// <summary>その日を含む区切りの開始日</summary>
    public static DateOnly Start(DateOnly date, DashboardPeriodUnit unit) => unit switch
    {
        DashboardPeriodUnit.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        DashboardPeriodUnit.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date,
    };

    /// <summary>区切りを count 個ずらした日（負なら過去へ）</summary>
    public static DateOnly Shift(DateOnly date, DashboardPeriodUnit unit, int count) => unit switch
    {
        DashboardPeriodUnit.Week => date.AddDays(7 * count),
        DashboardPeriodUnit.Month => date.AddMonths(count),
        _ => date.AddDays(count),
    };

    /// <summary>その日を含む区切りの最終日</summary>
    public static DateOnly End(DateOnly date, DashboardPeriodUnit unit) =>
        Shift(Start(date, unit), unit, 1).AddDays(-1);

    public static string UnitLabel(DashboardPeriodUnit unit) => unit switch
    {
        DashboardPeriodUnit.Week => "週次",
        DashboardPeriodUnit.Month => "月次",
        _ => "日次",
    };

    public static string AxisLabel(DashboardAxis axis) => axis switch
    {
        DashboardAxis.Process => "工程",
        DashboardAxis.Product => "品目",
        DashboardAxis.Shift => "直",
        DashboardAxis.Line => "ライン",
        DashboardAxis.WorkCenter => "作業区",
        _ => "設備",
    };
}
