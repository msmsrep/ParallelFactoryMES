namespace MesApp.Core.Contracts.Dashboard;

// ---- ダッシュボードの期間別・軸別集計（Spec.md 3.8。B-60-10-05 / C-40-10-05 / E-20-10-03）----

/// <summary>推移の区切り。週は月曜始まり、月は暦月。いずれも製造日（Spec.md 3.9）で区切る</summary>
public enum DashboardPeriodUnit
{
    Day,
    Week,
    Month,
}

/// <summary>内訳の軸</summary>
public enum DashboardAxis
{
    /// <summary>工程別（生産実績のみ）</summary>
    Process,
    /// <summary>品目別（生産実績のみ）</summary>
    Product,
    /// <summary>直別（生産実績のみ。記録時に固定した直）</summary>
    Shift,
    /// <summary>ライン別（作業区をライン段へまとめる。生産実績・稼働の両方）</summary>
    Line,
    /// <summary>作業区別（最下段。生産実績・稼働の両方）</summary>
    WorkCenter,
    /// <summary>設備別（稼働のみ）</summary>
    Equipment,
}

/// <summary>
/// 1つの区切り（期間または軸の値）の指標。
/// 生産実績から出す値（良品・不良）と稼働ログから出す値（稼働時間）は、
/// 軸や絞り込みによって片方しか求められないことがある。求められない側は null にする（0にしない。
/// 0にすると「実績が無い」と「その軸では数えられない」の区別が付かなくなる）。
/// </summary>
public record DashboardMetricsRow(
    /// <summary>区切りのキー（推移は期間の開始日 yyyy-MM-dd、内訳はコード）</summary>
    string Key,
    /// <summary>表示名</summary>
    string Label,
    /// <summary>推移のときの期間（指定期間で切り詰めた範囲）。内訳では null</summary>
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    decimal? GoodQuantity,
    decimal? DefectQuantity,
    /// <summary>不良率（%）＝不良数÷(良品数＋不良数)。産出が0なら null</summary>
    decimal? DefectRate,
    decimal? RunningHours,
    /// <summary>記録済み総時間（稼働・停止・段取り・故障・待機の合計）</summary>
    decimal? RecordedHours,
    /// <summary>時間稼働率（%）＝稼働時間÷記録済み総時間。記録が0なら null</summary>
    decimal? UtilizationRate,
    int? FailureCount);

/// <summary>
/// ダッシュボードの集計結果。<see cref="ProductionAvailable"/> / <see cref="UtilizationAvailable"/> が
/// false の指標は、その軸・絞り込みでは求められない（行の値も null）。
/// </summary>
public record DashboardSummaryResponse(
    DateOnly From,
    DateOnly To,
    DashboardMetricsRow Total,
    List<DashboardMetricsRow> Rows,
    bool ProductionAvailable,
    bool UtilizationAvailable);
