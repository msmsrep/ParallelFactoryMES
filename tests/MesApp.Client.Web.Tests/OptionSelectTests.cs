using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using MesApp.Client.Web.Shared;
using MesApp.Core.Contracts.Common;
using Microsoft.Extensions.DependencyInjection;

namespace MesApp.Client.Web.Tests;

/// <summary>
/// 検索して選ぶドロップダウン（<see cref="OptionSelect{TItem}"/>）。
/// 一覧のある画面すべてで対象（ロット・作業指示・出荷指示）の指定に使うため横断的に確認する。
/// </summary>
public class OptionSelectTests : BunitContext
{
    private readonly RecordingHandler _handler = new();

    public OptionSelectTests()
    {
        Services.AddSingleton(new HttpClient(_handler) { BaseAddress = new Uri("http://localhost/") });
    }

    private sealed record Item(int Id, string Code);

    private IRenderedComponent<OptionSelect<Item>> RenderSelect(Action<int>? onChanged = null) =>
        Render<OptionSelect<Item>>(p => p
            .Add(x => x.Endpoint, "api/things/options")
            .Add(x => x.ValueOf, i => i.Id)
            .Add(x => x.LabelOf, i => i.Code)
            .Add(x => x.ValueChanged, v => onChanged?.Invoke(v)));

    [Fact]
    public void 選択肢APIの内容を表示する()
    {
        _handler.Respond(new OptionsResult<Item>([new(1, "LOT-A"), new(2, "LOT-B")], false));

        var component = RenderSelect();

        Assert.Contains("LOT-A", component.Markup, StringComparison.Ordinal);
        Assert.Contains("LOT-B", component.Markup, StringComparison.Ordinal);
        Assert.Equal("api/things/options", _handler.LastPath);
    }

    [Fact]
    public void 検索語をqとしてサーバーへ渡す()
    {
        _handler.Respond(new OptionsResult<Item>([new(1, "LOT-A")], false));
        var component = RenderSelect();

        component.Find("input[type=search]").Input("LOT-A");

        Assert.Equal("api/things/options?q=LOT-A", _handler.LastPath);
    }

    [Fact]
    public void 既にクエリを持つエンドポイントにも検索語を足せる()
    {
        _handler.Respond(new OptionsResult<Item>([], false));
        var component = Render<OptionSelect<Item>>(p => p
            .Add(x => x.Endpoint, "api/things/options?status=Instructed")
            .Add(x => x.ValueOf, i => i.Id)
            .Add(x => x.LabelOf, i => i.Code));

        component.Find("input[type=search]").Input("SH-1");

        Assert.Equal("api/things/options?status=Instructed&q=SH-1", _handler.LastPath);
    }

    [Fact]
    public void 上限を超えたら黙って切らずに絞り込みを促す()
    {
        _handler.Respond(new OptionsResult<Item>([new(1, "LOT-A")], true));

        var component = RenderSelect();

        Assert.Contains("絞り込んでください", component.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void 選択した値を通知する()
    {
        _handler.Respond(new OptionsResult<Item>([new(7, "LOT-G")], false));
        var selected = 0;
        var component = RenderSelect(v => selected = v);

        component.Find("select").Change("7");

        Assert.Equal(7, selected);
    }

    /// <summary>直近のリクエストURLを覚えつつ、決めた選択肢を返すハンドラ</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private string _json = "{\"items\":[],\"truncated\":false}";

        public string? LastPath { get; private set; }

        public void Respond<T>(OptionsResult<T> result) =>
            _json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri!.PathAndQuery.TrimStart('/');
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
