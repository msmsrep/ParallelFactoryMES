using System.Globalization;
using System.Security.Claims;
using System.Text;
using MesApp.Api.Localization;
using MesApp.Core.Abstractions;
using MesApp.Core.Contracts.Integration;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace MesApp.Api.Services.FolderLink;

/// <summary>
/// フォルダ連携（CSVの自動取込。Spec.md 3.8 フォルダ連携。ERP等からの指図の取込 A-20-10-01 ほか）。
/// <para>
/// 連携フォルダの <c>inbox</c> に置かれた CSV・ZIP を、画面からの取込と<b>同じ処理</b>
/// （<see cref="MasterCsvService"/>・<see cref="ActualCsvService"/>）で取り込み、結果に応じて
/// <c>processed</c> か <c>error</c> へ移して、横に結果ファイル（<c>.result.txt</c>）を書く。
/// 取込の可否・記録者・監査ログは設定した利用者（<see cref="FolderLinkOptions.UserName"/>）で判定する。
/// </para>
/// <para>
/// 種別は <c>inbox/&lt;種別&gt;/</c> のフォルダ名か、<c>inbox</c> 直下ならファイル名（<see cref="CsvBundle.KindOf"/>）で決める。
/// 他システムの出力するファイル名を変えられないときは、種別のフォルダに置かせる。
/// </para>
/// </summary>
public sealed class FolderLinkService(
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor httpContextAccessor,
    IOptions<FolderLinkOptions> options,
    IHostEnvironment environment,
    IBusinessDateService businessDate,
    ILogger<FolderLinkService> logger)
{
    public const string InboxFolder = "inbox";
    public const string ProcessedFolder = "processed";
    public const string ErrorFolder = "error";
    public const string ResultSuffix = ".result.txt";

    /// <summary>画面に残す最近の結果の件数</summary>
    private const int RecentLimit = 50;

    /// <summary>結果1件に載せるメッセージの上限（全文は結果ファイルにある）</summary>
    private const int MessageLimit = 20;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _statusLock = new();
    private readonly LinkedList<FolderLinkFileResult> _recent = new();
    private string? _configError;
    private DateTimeOffset? _lastScanAt;

    private FolderLinkOptions Options => options.Value;

    /// <summary>連携フォルダの絶対パス（未設定なら null）</summary>
    public string? RootPath => string.IsNullOrWhiteSpace(Options.Root)
        ? null
        : Path.GetFullPath(Options.Root, environment.ContentRootPath);

    public bool Enabled => RootPath is not null;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(5, Options.PollSeconds));

    public FolderLinkStatusResponse Status()
    {
        lock (_statusLock)
        {
            return new FolderLinkStatusResponse(
                Enabled, RootPath, Options.UserName, (int)PollInterval.TotalSeconds,
                _configError, _lastScanAt, [.. _recent]);
        }
    }

    /// <summary>
    /// inbox を1回走査し、取り込めるファイルをすべて処理する。同時に走らないよう直列にする。
    /// <para>
    /// 実行の文脈（AsyncLocal）を引き継がずに走らせる。取込では監査ログが利用者を
    /// <see cref="IHttpContextAccessor"/> から読むため連携用の利用者を立てるが、画面の「今すぐ取り込む」から
    /// 呼ばれたときにそのまま差し替えると、呼び出し元のリクエストの文脈を壊してしまう。
    /// </para>
    /// </summary>
    public Task ScanAsync(CancellationToken ct)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(() => ScanCoreAsync(ct), ct);
        }
    }

    private async Task ScanCoreAsync(CancellationToken ct)
    {
        if (RootPath is not { } root)
        {
            return;
        }
        await _gate.WaitAsync(ct);
        try
        {
            // 文脈を引き継がないので、ここで決めないと OS の表示言語になる（Task.Run の中だけに効く）
            CultureInfo.CurrentUICulture = new CultureInfo(MesCultures.Resolve(Options.Language));
            var configError = (string?)null;
            try
            {
                Directory.CreateDirectory(Path.Combine(root, InboxFolder));
                Directory.CreateDirectory(Path.Combine(root, ProcessedFolder));
                Directory.CreateDirectory(Path.Combine(root, ErrorFolder));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                configError = ApiText.T("連携フォルダを作れません：{0}", ex.Message);
            }

            ClaimsPrincipal? principal = null;
            if (configError is null)
            {
                (principal, configError) = await ResolveUserAsync(ct);
            }
            SetScanState(configError);
            if (configError is not null || principal is null)
            {
                return;
            }

            foreach (var file in FindReadyFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                await ProcessFileAsync(root, file, principal, ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SetScanState(string? configError)
    {
        lock (_statusLock)
        {
            if (configError is not null && configError != _configError)
            {
                logger.LogWarning("フォルダ連携の設定に誤りがあります：{Error}", configError);
            }
            _configError = configError;
            _lastScanAt = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>取込を行う利用者を引き、画面からログインしたときと同じクレーム（ID・ユーザー名・ロール）を組み立てる</summary>
    private async Task<(ClaimsPrincipal? Principal, string? Error)> ResolveUserAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Options.UserName))
        {
            return (null, ApiText.T("取込を行う利用者（FolderLink:UserName）が設定されていません。"));
        }
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByNameAsync(Options.UserName);
        if (user is null || !user.IsActive)
        {
            return (null, ApiText.T("取込を行う利用者 '{0}' がいないか、無効です。", Options.UserName));
        }
        if (await users.IsLockedOutAsync(user))
        {
            return (null, ApiText.T("取込を行う利用者 '{0}' はロックされています。", Options.UserName));
        }
        ct.ThrowIfCancellationRequested();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? Options.UserName),
        };
        claims.AddRange((await users.GetRolesAsync(user)).Select(r => new Claim(ClaimTypes.Role, r)));
        return (new ClaimsPrincipal(new ClaimsIdentity(claims, "FolderLink", ClaimTypes.Name, ClaimTypes.Role)), null);
    }

    /// <summary>
    /// 取り込んでよいファイル（inbox 直下と種別フォルダの直下の .csv・.zip）を、置かれた順（更新日時、同じなら名前）に返す。
    /// 書き込みから間もないものは次の走査に回す
    /// </summary>
    private List<FileInfo> FindReadyFiles(string root)
    {
        var inbox = new DirectoryInfo(Path.Combine(root, InboxFolder));
        var settledBefore = DateTime.UtcNow.AddSeconds(-Math.Max(0, Options.SettleSeconds));
        var files = inbox.EnumerateFiles()
            .Concat(inbox.EnumerateDirectories().SelectMany(d => d.EnumerateFiles("*.csv")));
        return [.. files
            .Where(f => IsTarget(f.Name) && f.LastWriteTimeUtc <= settledBefore)
            .OrderBy(f => f.LastWriteTimeUtc)
            .ThenBy(f => f.FullName, StringComparer.Ordinal)];
    }

    private static bool IsTarget(string name) =>
        name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private async Task ProcessFileAsync(string root, FileInfo file, ClaimsPrincipal principal, CancellationToken ct)
    {
        var inbox = Path.Combine(root, InboxFolder);
        var relative = Path.GetRelativePath(inbox, file.FullName);

        byte[]? bytes = null;
        try
        {
            // 他のプログラムが書き込み中なら開けない。次の走査でもう一度試す
            await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
            if (stream.Length <= (IsZip(file.Name) ? CsvBundle.MaxZipBytes : CsvImport.MaxUploadBytes))
            {
                bytes = new byte[stream.Length];
                await stream.ReadExactlyAsync(bytes, ct);
            }
        }
        catch (IOException)
        {
            return;
        }

        var startedAt = DateTimeOffset.UtcNow;
        Outcome outcome;
        try
        {
            outcome = bytes is null
                ? Outcome.Fail(IsZip(file.Name) ? "zip" : "", ApiText.T("ファイルが大きすぎます（CSVは{0}MB、ZIPは{1}MBまで）。",
                    CsvImport.MaxUploadBytes / 1024 / 1024, CsvBundle.MaxZipBytes / 1024 / 1024))
                : await ImportAsync(file, relative, bytes, principal, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 想定外の例外でも、ファイルを inbox に残すと毎回同じ失敗を繰り返すので error へ移す
            logger.LogError(ex, "フォルダ連携の取込で例外が発生しました：{File}", relative);
            outcome = Outcome.Fail("", ApiText.T("取込中に想定外のエラーが発生しました：{0}", ex.Message));
        }

        var movedTo = MoveWithResult(root, file, relative, startedAt, outcome);
        var result = new FolderLinkFileResult(
            relative.Replace('\\', '/'), startedAt, outcome.Succeeded, outcome.Kind,
            outcome.Rows, outcome.Created, outcome.Updated, movedTo, [.. outcome.Messages.Take(MessageLimit)]);
        lock (_statusLock)
        {
            _recent.AddFirst(result);
            while (_recent.Count > RecentLimit)
            {
                _recent.RemoveLast();
            }
        }
        logger.LogInformation("フォルダ連携：{File} を {Result}（{Rows}行、登録{Created}・更新{Updated}）",
            result.FileName, result.Succeeded ? "取り込みました" : "取り込めませんでした", result.Rows, result.Created, result.Updated);
    }

    private static bool IsZip(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 取込そのもの。1ファイルごとにスコープを作り、連携用の利用者を HTTP の文脈に立ててから既存の取込を呼ぶ
    /// （監査ログの記録者と、実績の記録者が画面から取り込んだときと同じになる）
    /// </summary>
    private async Task<Outcome> ImportAsync(
        FileInfo file, string relative, byte[] bytes, ClaimsPrincipal principal, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        httpContextAccessor.HttpContext = new DefaultHttpContext { User = principal, RequestServices = scope.ServiceProvider };
        try
        {
            var intake = scope.ServiceProvider.GetRequiredService<CsvIntake>();
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (IsZip(file.Name))
            {
                return await ImportZipAsync(intake, bytes, principal, userId, ct);
            }

            // 種別フォルダに置かれたものはフォルダ名、直下に置かれたものはファイル名で種別を決める
            var folder = Path.GetDirectoryName(relative);
            var kindName = string.IsNullOrEmpty(folder) ? CsvBundle.KindOf(file.Name) : folder;
            if (CsvIntake.Find(kindName) is { } kind)
            {
                return CsvIntake.CanWrite(principal, kind)
                    ? Outcome.Of(await intake.ImportAsync(kind, CsvFile.Decode(bytes), dryRun: false, userId, ct))
                    : Outcome.Fail(kind.Info.Kind, NoPermission(kind.Info.Label));
            }
            return Outcome.Fail(kindName, ApiText.T(
                "CSVの種別 '{0}' が分かりません。ファイル名を「種別.csv」（例 manufacturing-orders.csv）にするか、inbox の下の種別の名前のフォルダに置いてください。",
                kindName));
        }
        finally
        {
            httpContextAccessor.HttpContext = null;
        }
    }

    private string NoPermission(string label) =>
        ApiText.T("利用者 '{0}' には「{1}」を取り込む権限がありません。", Options.UserName, ApiText.T(label));

    /// <summary>
    /// ZIP の一括取込。中身がすべてマスタならマスタ、すべて実績なら実績として、画面の一括取込と同じく
    /// 全ファイルを1つのトランザクションで取り込む（混ざっていたら受け付けない）
    /// </summary>
    private async Task<Outcome> ImportZipAsync(
        CsvIntake intake, byte[] bytes, ClaimsPrincipal principal, string? userId, CancellationToken ct)
    {
        if (CsvBundle.ReadZip(bytes, out var zipError) is not { } entries)
        {
            return Outcome.Fail("zip", zipError ?? "");
        }
        // 取込先は中身から決める（すべてマスタならマスタ、すべて実績なら実績。混在・生産計画入りは拒否）
        if (CsvIntake.ResolveBundle(entries, area: null, out var error) is not { } bundle)
        {
            return Outcome.Fail("zip", error ?? "");
        }
        if (CsvIntake.FirstDenied(principal, bundle) is { } denied)
        {
            return Outcome.Fail("zip", NoPermission(denied.Info.Label));
        }
        return Outcome.Of(await intake.ImportBundleAsync(bundle, dryRun: false, userId, ct));
    }

    /// <summary>
    /// processed か error の <c>暦日/時刻/</c> の下へ、inbox と同じ相対パスで移し、横に結果ファイルを書く
    /// （日付は製造日ではなく工場の暦日。監査ログと同じく、業務の1日ではなくファイルを探す手がかりなので）。
    /// ファイル名を変えないので、直してから inbox の同じ場所へ戻せばそのまま取り込み直せる
    /// </summary>
    private string MoveWithResult(string root, FileInfo file, string relative, DateTimeOffset at, Outcome outcome)
    {
        var local = businessDate.ToFactoryTime(at);
        var baseDir = Path.Combine(root, outcome.Succeeded ? ProcessedFolder : ErrorFolder, $"{local:yyyyMMdd}");
        var stamp = $"{local:HHmmss}";
        var destination = Path.Combine(baseDir, stamp, relative);
        for (var n = 2; File.Exists(destination); n++)
        {
            destination = Path.Combine(baseDir, $"{stamp}-{n}", relative);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            file.MoveTo(destination);
            File.WriteAllText(destination + ResultSuffix, FormatResult(relative, local, outcome), new UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 移せないと次の走査で同じファイルをもう一度取り込んでしまう。取り込めた場合は特に危ないので強く残す
            logger.LogError(ex, "フォルダ連携：{File} を {Destination} へ移せませんでした。二重取込を防ぐため inbox から手で取り除いてください。",
                relative, destination);
            return "";
        }
        return Path.GetRelativePath(root, destination).Replace('\\', '/');
    }

    private static string FormatResult(string relative, DateTimeOffset local, Outcome outcome)
    {
        var text = new StringBuilder();
        text.AppendLine(ApiText.T("ファイル：{0}", relative.Replace('\\', '/')));
        text.AppendLine(ApiText.T("処理日時：{0}", $"{local:yyyy-MM-dd HH:mm:ss zzz}"));
        text.AppendLine(outcome.Succeeded
            ? ApiText.T("結果：取り込みました")
            : ApiText.T("結果：取り込めませんでした（何も反映していません）"));
        text.AppendLine(ApiText.T("種別：{0}", outcome.Kind));
        text.AppendLine(ApiText.T("データ行：{0}　登録：{1}　更新：{2}", outcome.Rows, outcome.Created, outcome.Updated));
        if (outcome.Messages.Count > 0)
        {
            text.AppendLine();
            foreach (var message in outcome.Messages)
            {
                text.AppendLine(message);
            }
        }
        return text.ToString();
    }

    /// <summary>1ファイルの取込結果（単体CSVもZIPも同じ形にまとめる）</summary>
    private sealed record Outcome(bool Succeeded, string Kind, int Rows, int Created, int Updated, List<string> Messages)
    {
        public static Outcome Fail(string kind, string message) => new(false, kind, 0, 0, 0, [message]);

        public static Outcome Of(CsvImportResult result) => new(
            result.Succeeded, result.Kind, result.DataRows, result.Created, result.Updated,
            [.. Lines(null, result)]);

        public static Outcome Of(CsvBundleImportResult bundle)
        {
            var messages = bundle.Files.SelectMany(f => Lines(f.FileName, f.Result)).ToList();
            if (bundle.NotProcessed.Count > 0)
            {
                messages.Add(ApiText.T("処理しなかったファイル：{0}", string.Join(ApiText.T("、"), bundle.NotProcessed)));
            }
            return new(bundle.Succeeded, "zip",
                bundle.Files.Sum(f => f.Result.DataRows),
                bundle.Succeeded ? bundle.Files.Sum(f => f.Result.Created) : 0,
                bundle.Succeeded ? bundle.Files.Sum(f => f.Result.Updated) : 0,
                messages);
        }

        /// <summary>エラーのあとに警告を並べる。行番号0は行に依らないメッセージ</summary>
        private static IEnumerable<string> Lines(string? fileName, CsvImportResult result)
        {
            var prefix = fileName is null ? "" : $"{fileName} ";
            foreach (var error in result.Errors)
            {
                yield return prefix + (error.Line > 0
                    ? ApiText.T("{0}行目：{1}", error.Line, error.Message)
                    : error.Message);
            }
            foreach (var warning in result.Warnings)
            {
                yield return prefix + ApiText.T("警告 {0}行目：{1}", warning.Line, warning.Message);
            }
        }
    }
}
