using MesApp.Client.Web;
using MesApp.Client.Web.Auth;
using MesApp.Client.Web.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);

builder.Services.AddAuthorizationCore();
builder.Services.AddLocalization();
builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<ApiAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<ApiAuthenticationStateProvider>());

// 認証系専用のHttpClient（Bearer付与ハンドラを通さない素のクライアント）
builder.Services.AddScoped(sp => new AuthService(
    WithLanguage(new HttpClient { BaseAddress = baseAddress }),
    sp.GetRequiredService<TokenStore>(),
    sp.GetRequiredService<ApiAuthenticationStateProvider>()));

// API呼び出し用HttpClient（Bearer自動付与＋401時サイレントリフレッシュ）
builder.Services.AddScoped(sp =>
{
    var handler = new AuthMessageHandler(
        sp.GetRequiredService<TokenStore>(),
        sp.GetRequiredService<AuthService>(),
        sp.GetRequiredService<NavigationManager>())
    {
        InnerHandler = new HttpClientHandler(),
    };
    return WithLanguage(new HttpClient(handler) { BaseAddress = baseAddress });
});

var host = builder.Build();

// 表示言語を先に決める（HttpClient の Accept-Language と訳の読み込みがこれに従う。Spec.md 7.9）
await AppCulture.ApplyStoredAsync(host.Services.GetRequiredService<IJSRuntime>());

// 起動時のサイレントリフレッシュ（リフレッシュCookieが残っていればセッション復元）
await host.Services.GetRequiredService<AuthService>().InitializeAsync();

await host.RunAsync();

// APIの応答（エラー文言）を画面と同じ言語で受け取る。言語の切替は再読込を伴うので、生成時の値で足りる
static HttpClient WithLanguage(HttpClient client)
{
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(System.Globalization.CultureInfo.CurrentUICulture.Name);
    return client;
}
