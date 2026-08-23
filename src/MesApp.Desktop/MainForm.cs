using MesApp.Api;
using MesApp.Infrastructure;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MesApp.Desktop;

/// <summary>
/// MESのメインウィンドウ。プロセス内でKestrelをループバックアドレスに起動し、
/// その画面（Spec.md 2.1のBlazor WASMクライアント）をWebView2で表示する。
/// </summary>
internal sealed class MainForm : Form
{
    /// <summary>初回起動はマイグレーションとシードが走るため長めに取る</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    private readonly string[] _args;

    private readonly WebView2 _webView = new()
    {
        Dock = DockStyle.Fill,
        Visible = false,
    };

    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "起動しています…",
    };

    private WebApplication? _app;

    public MainForm(string[] args)
    {
        _args = args;

        Text = "Parallel Factory MES";
        MinimumSize = new Size(1024, 640);
        Size = new Size(1440, 900);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        Controls.Add(_webView);
        Controls.Add(_status);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            SetStatus("データベースを準備しています…");

            // UIスレッドのSynchronizationContext上でホストの起動を待つとデッドロックし得るため、
            // 起動処理はスレッドプールで動かす（ウィンドウが出たまま無反応になるのを防ぐ）。
            var startup = Task.Run(StartApiAsync);
            if (await Task.WhenAny(startup, Task.Delay(StartupTimeout)) != startup)
            {
                throw new TimeoutException(
                    $"{StartupTimeout.TotalMinutes:0}分以内にローカルサーバーを起動できませんでした。");
            }

            var address = await startup;
            StartupLog.Write($"APIの起動完了: {address}");

            SetStatus("画面を読み込んでいます…");
            await ShowClientAsync(address);
            StartupLog.Write("画面の表示完了");
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            StartupLog.WriteException("WebView2ランタイムが見つからない", ex);
            ShowStartupFailure(
                "表示に必要な Microsoft Edge WebView2 ランタイムが見つかりません。\n"
                + "Microsoft のサイトから WebView2 ランタイムをインストールしてから、もう一度起動してください。");
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("起動に失敗", ex);
            ShowStartupFailure(
                $"起動に失敗しました。\n\n{ex.Message}\n\n"
                + $"詳細は次のファイルに記録されています:\n{Path.Combine(MesAppDataDirectory.Current, "startup.log")}");
        }
    }

    /// <summary>プロセス内でKestrelを起動し、実際に割り当てられたループバックURLを返す</summary>
    private async Task<string> StartApiAsync()
    {
        // ポート0でOSに空きポートを選ばせる（現場PCで使用中ポートと衝突しないようにするため）。
        // コンテンツルートは実行ファイルの場所に固定する（MSIXでは起動時の作業ディレクトリが一定しないため）。
        string[] hostArgs =
        [
            .. _args,
            "--contentRoot", AppContext.BaseDirectory,
            "--urls", "http://127.0.0.1:0",
        ];

        StartupLog.Write("Webアプリを組み立てます");
        _app = MesAppHost.Build(hostArgs);

        StartupLog.Write("データベースを初期化します");
        await MesAppHost.InitializeAsync(_app);

        StartupLog.Write("Kestrelを起動します");
        await _app.StartAsync();

        var addresses = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;

        return addresses?.FirstOrDefault()
               ?? throw new InvalidOperationException("ローカルサーバーのアドレスを取得できませんでした。");
    }

    /// <summary>WebView2を初期化してクライアント画面を表示する</summary>
    private async Task ShowClientAsync(string address)
    {
        // ユーザーデータフォルダーは既定では実行ファイルの隣に作られる。
        // MSIXのインストール先は読み取り専用なので、書き込み可能なデータディレクトリを明示する。
        StartupLog.Write("WebView2を初期化します");
        var environment = await CoreWebView2Environment.CreateAsync(
            userDataFolder: Path.Combine(MesAppDataDirectory.Current, "WebView2"));

        await _webView.EnsureCoreWebView2Async(environment);

        var settings = _webView.CoreWebView2.Settings;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;

        // 外部リンク（ヘルプ等）はアプリ内ではなく既定のブラウザーで開く
        _webView.CoreWebView2.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenInDefaultBrowser(e.Uri);
        };

        _webView.CoreWebView2.Navigate(address);

        _status.Visible = false;
        _webView.Visible = true;
        _webView.Focus();
    }

    private void SetStatus(string message)
    {
        StartupLog.Write(message);
        _status.Text = message;
        _status.Refresh();
    }

    private static void OpenInDefaultBrowser(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(parsed.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
    }

    private void ShowStartupFailure(string message)
    {
        _webView.Visible = false;
        _status.Text = message;
        _status.Visible = true;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        var app = Interlocked.Exchange(ref _app, null);
        if (app is null)
        {
            return;
        }

        StartupLog.Write("終了処理を開始します");
        try
        {
            // ウィンドウを閉じたらSQLiteの書き込みを確実に終わらせてからプロセスを終了する。
            // ただしUIスレッド上で待つと StopAsync の継続がUIスレッドを必要として詰まり、
            // プロセスが終了できなくなる（ウィンドウなしのプロセスが残り続ける）。
            // 継続がスレッドプールで動くよう Task.Run の中で待つ。
            var shutdown = Task.Run(async () =>
            {
                await app.StopAsync(TimeSpan.FromSeconds(5));
                await app.DisposeAsync();
            });

            if (!shutdown.Wait(TimeSpan.FromSeconds(10)))
            {
                StartupLog.Write("終了処理が時間内に完了しませんでした。プロセスを終了します");
            }
        }
        catch (Exception ex)
        {
            StartupLog.WriteException("終了処理に失敗", ex);
        }

        StartupLog.Write("終了処理を完了しました");
    }
}
