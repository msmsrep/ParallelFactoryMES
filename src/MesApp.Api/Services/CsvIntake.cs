using System.Security.Claims;
using MesApp.Api.Localization;
using MesApp.Core.Contracts.Masters;

namespace MesApp.Api.Services;

/// <summary>CSVの取込先（マスタCSVか実績CSVか）</summary>
public enum CsvIntakeArea
{
    Master,
    Actual,
}

/// <summary>取込先の決まった1種別</summary>
/// <param name="Actual">実績CSVのときの種別（マスタCSVなら null）</param>
/// <param name="WriteRoles">取込に要るロールグループ（単票APIと同じ定数）</param>
public sealed record CsvIntakeKind(CsvIntakeArea Area, CsvKindInfo Info, ActualCsvKind? Actual, string WriteRoles);

/// <summary>取込先の決まったZIP（マスタだけか実績だけ）</summary>
public sealed record CsvIntakeBundle(CsvIntakeArea Area, IReadOnlyList<CsvBundleEntry> Entries, IReadOnlyList<CsvIntakeKind> Kinds);

/// <summary>
/// CSV取込の受付（Spec.md 3.8）。種別の解決・ZIPの振り分け・取込権限の判定・取込の実行を1か所に置き、
/// 画面（<c>MasterCsvController</c>・<c>ActualCsvController</c>）、フォルダ連携、連携API の3つの入口で共有する。
/// 入口ごとに書くと、受け付ける種別や権限の判定が入口によってずれる
/// </summary>
public sealed class CsvIntake(MasterCsvService masters, ActualCsvService actuals)
{
    /// <summary>
    /// 種別を引く。<paramref name="area"/> を指定すればその取込先だけ、null ならマスタ→実績の順に探す
    /// （種別名はマスタと実績で重ならない）
    /// </summary>
    public static CsvIntakeKind? Find(string kind, CsvIntakeArea? area = null)
    {
        if ((area is null or CsvIntakeArea.Master) && MasterCsvKinds.Find(kind) is { } master)
        {
            return new CsvIntakeKind(CsvIntakeArea.Master, master, null, MasterCsvKinds.WriteRolesOf(master));
        }
        if ((area is null or CsvIntakeArea.Actual) && ActualCsvKinds.Find(kind) is { } actual)
        {
            return new CsvIntakeKind(CsvIntakeArea.Actual, actual.Info, actual, actual.WriteRoles);
        }
        return null;
    }

    /// <summary>取り込む権限があるか（画面からの取込と同じ判定）</summary>
    public static bool CanWrite(ClaimsPrincipal user, CsvIntakeKind kind) => MesRoleGroups.IsInGroup(user, kind.WriteRoles);

    /// <summary>ZIPの中で、取り込む権限の無い最初の種別（全部取り込めるなら null）</summary>
    public static CsvIntakeKind? FirstDenied(ClaimsPrincipal user, CsvIntakeBundle bundle) =>
        bundle.Kinds.FirstOrDefault(k => !CanWrite(user, k));

    /// <summary>
    /// ZIPの中身を、取込先ごとに受け付けられるか確かめる。<paramref name="area"/> を指定すれば画面と同じ文面で、
    /// null（フォルダ連携）なら中身から取込先を決める。受け付けられなければ null とエラー文
    /// </summary>
    public static CsvIntakeBundle? ResolveBundle(IReadOnlyList<CsvBundleEntry> entries, CsvIntakeArea? area, out string? error)
    {
        error = null;
        // 生産計画などマスタでない種別は、種別として取り込めてもマスタのZIPには入れさせない（それぞれの画面から取り込む）
        var standalone = entries
            .Where(e => MasterCsvKinds.Standalone.Contains(e.Kind, StringComparer.OrdinalIgnoreCase)).ToList();
        var masterKinds = entries.Select(e => MasterCsvKinds.Find(e.Kind)).ToList();
        var actualKinds = entries.Select(e => ActualCsvKinds.Find(e.Kind)).ToList();

        switch (area)
        {
            case CsvIntakeArea.Master:
                if (standalone.Count > 0)
                {
                    error = ApiText.T(
                        "マスタでないCSVが含まれています：{0}。生産計画は生産計画・予実画面の計画登録タブから取り込んでください。",
                        string.Join(ApiText.T("、"), standalone.Select(e => e.FileName)));
                    return null;
                }
                if (masterKinds.Any(k => k is null))
                {
                    var unknown = entries.Where((_, i) => masterKinds[i] is null).ToList();
                    var actual = unknown.Where(e => ActualCsvKinds.Find(e.Kind) is not null).ToList();
                    error = actual.Count > 0
                        ? ApiText.T("実績のCSVが含まれています：{0}。実績は実績CSV取込で別のZIPとして取り込んでください。",
                            string.Join(ApiText.T("、"), actual.Select(e => e.FileName)))
                        : ApiText.T("取込に対応していないマスタのCSVが含まれています：{0}（ファイル名は「番号_種別.csv」。例 01_work-centers.csv）。",
                            string.Join(ApiText.T("、"), unknown.Select(e => e.FileName)));
                    return null;
                }
                return MasterBundle(entries, masterKinds);

            case CsvIntakeArea.Actual:
                if (actualKinds.Any(k => k is null))
                {
                    var unknown = entries.Where((_, i) => actualKinds[i] is null).ToList();
                    var master = unknown.Where(e => MasterCsvKinds.Find(e.Kind) is not null).ToList();
                    error = master.Count > 0
                        ? ApiText.T("マスタのCSVが含まれています：{0}。マスタはマスタ管理で別のZIPとして取り込んでください。",
                            string.Join(ApiText.T("、"), master.Select(e => e.FileName)))
                        : ApiText.T("取込に対応していない実績のCSVが含まれています：{0}（ファイル名は「番号_種別.csv」。例 01_receiving.csv）。",
                            string.Join(ApiText.T("、"), unknown.Select(e => e.FileName)));
                    return null;
                }
                return ActualBundle(entries, actualKinds);

            default:
                if (standalone.Count == 0 && masterKinds.All(k => k is not null))
                {
                    return MasterBundle(entries, masterKinds);
                }
                if (actualKinds.All(k => k is not null))
                {
                    return ActualBundle(entries, actualKinds);
                }
                var unknownAny = entries.Where((_, i) => masterKinds[i] is null && actualKinds[i] is null)
                    .Select(e => e.FileName).ToList();
                error = unknownAny.Count > 0
                    ? ApiText.T("ZIPに種別の分からないCSVが含まれています：{0}（ファイル名は「番号_種別.csv」。例 01_receiving.csv）。",
                        string.Join(ApiText.T("、"), unknownAny))
                    : ApiText.T("ZIPには、マスタだけか実績だけのCSVを入れてください。生産計画はZIPに入れず、1ファイルずつ置いてください。");
                return null;
        }
    }

    private static CsvIntakeBundle MasterBundle(IReadOnlyList<CsvBundleEntry> entries, List<CsvKindInfo?> kinds) =>
        new(CsvIntakeArea.Master, entries,
            [.. kinds.Select(k => new CsvIntakeKind(CsvIntakeArea.Master, k!, null, MasterCsvKinds.WriteRolesOf(k!)))]);

    private static CsvIntakeBundle ActualBundle(IReadOnlyList<CsvBundleEntry> entries, List<ActualCsvKind?> kinds) =>
        new(CsvIntakeArea.Actual, entries,
            [.. kinds.Select(k => new CsvIntakeKind(CsvIntakeArea.Actual, k!.Info, k, k.WriteRoles))]);

    /// <summary>1ファイルを取り込む（権限は呼び出し側で確かめておく）。実績の記録者は <paramref name="userId"/></summary>
    public Task<CsvImportResult> ImportAsync(CsvIntakeKind kind, string csv, bool dryRun, string? userId, CancellationToken ct) =>
        kind.Area == CsvIntakeArea.Master
            ? masters.ImportAsync(kind.Info, csv, dryRun, ct)
            : actuals.ImportAsync(kind.Actual!, csv, dryRun, userId, ct);

    /// <summary>ZIPを取り込む（全ファイルを1つのトランザクションで。権限は呼び出し側で確かめておく）</summary>
    public Task<CsvBundleImportResult> ImportBundleAsync(CsvIntakeBundle bundle, bool dryRun, string? userId, CancellationToken ct) =>
        bundle.Area == CsvIntakeArea.Master
            ? masters.ImportBundleAsync(
                [.. bundle.Entries.Select((e, i) => new CsvBundleFile<CsvKindInfo>(e.FileName, bundle.Kinds[i].Info, e.Text))], dryRun, ct)
            : actuals.ImportBundleAsync(
                [.. bundle.Entries.Select((e, i) => new CsvBundleFile<ActualCsvKind>(e.FileName, bundle.Kinds[i].Actual!, e.Text))], dryRun, userId, ct);
}
