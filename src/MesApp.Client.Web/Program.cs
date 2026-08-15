using MesApp.Client.Web;
using MesApp.Client.Web.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);

builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<ApiAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(
    sp => sp.GetRequiredService<ApiAuthenticationStateProvider>());

// 認証系専用のHttpClient（Bearer付与ハンドラを通さない素のクライアント）
builder.Services.AddScoped(sp => new AuthService(
    new HttpClient { BaseAddress = baseAddress },
    sp.GetRequiredService<TokenStore>(),
    sp.GetRequiredService<ApiAuthenticationStateProvider>()));

// API呼び出し用HttpClient（Bearer自動付与＋401時サイレントリフレッシュ）
builder.Services.AddScoped(sp =>
{
    var handler = new AuthMessageHandler(
        sp.GetRequiredService<TokenStore>(),
        sp.GetRequiredService<AuthService>())
    {
        InnerHandler = new HttpClientHandler(),
    };
    return new HttpClient(handler) { BaseAddress = baseAddress };
});

var host = builder.Build();

// 起動時のサイレントリフレッシュ（リフレッシュCookieが残っていればセッション復元）
await host.Services.GetRequiredService<AuthService>().InitializeAsync();

await host.RunAsync();
