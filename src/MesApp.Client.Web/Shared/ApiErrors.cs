using System.Net.Http.Json;

namespace MesApp.Client.Web.Shared;

/// <summary>
/// API の失敗応答を画面に出すメッセージへ変換する（Spec.md 3.9 競合時の応答）。
/// サーバーは ProblemDetails の Title に日本語メッセージを入れて返すため、それを優先し、
/// 読めないとき（本文なし・JSON 以外）だけ画面側の既定文言を使う。
/// </summary>
public static class ApiErrors
{
    private sealed record ProblemDto(string? Title);

    /// <summary>失敗応答ならエラーメッセージを、成功応答なら null を返す</summary>
    public static async Task<string?> ReadErrorAsync(this HttpResponseMessage response, string fallback)
    {
        if (response.IsSuccessStatusCode)
        {
            return null;
        }
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
            return problem?.Title ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
