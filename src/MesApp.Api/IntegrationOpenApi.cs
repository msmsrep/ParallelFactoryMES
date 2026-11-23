using System.Text.Json.Nodes;
using MesApp.Api.Services.Webhooks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace MesApp.Api;

/// <summary>
/// 連携API の OpenAPI 文書（Spec.md 3.8 連携API。docs-dev/IntegrationApiTasks.md INT-04）。
/// <para>
/// 連携API の Controller（<c>[ApiExplorerSettings(GroupName = IntegrationOpenApi.DocumentName)]</c>）だけを集め、
/// <c>/openapi/integration-v1.json</c> として<b>全環境で</b>出す（利用者が Postman・Visual Studio などに読み込んで連携を組めるように。
/// 中身は API の形だけで秘密は無いので認証は要らない）。画面用の API は外部との約束にしないので載せない
/// （開発環境だけの既定の文書 <c>/openapi/v1.json</c> に残る）。
/// </para>
/// </summary>
public static class IntegrationOpenApi
{
    /// <summary>文書の名前（URL の一部）。Controller の GroupName にも使う</summary>
    public const string DocumentName = "integration-v1";

    public static IServiceCollection AddIntegrationOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            options.ShouldInclude = description => description.GroupName == DocumentName;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Parallel Factory MES 連携API",
                    Version = "v1",
                    Description =
                        "販売管理・生産管理（ERP）や社内のスクリプトから MES とデータをやり取りする API。" +
                        "要求のヘッダ X-Api-Key に APIキー（管理 → APIキー で発行）を付けて呼ぶ。" +
                        "v1 の中では項目を足すだけで、消す・名前や意味を変えるときは v2 を作る（CSV の列キーも同じ）。",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[ApiKeyAuthentication.Scheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = ApiKeyAuthentication.HeaderName,
                    Description = "管理 → APIキー で発行したキー（pfm_ で始まる）。権限はキーに紐づく利用者のロールで判定する",
                };
                document.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(ApiKeyAuthentication.Scheme, document)] = [],
                    },
                ];
                AddWebhooks(document);
                return Task.CompletedTask;
            });
        });

    /// <summary>
    /// MES から外へ送る通知（Spec.md 3.8 Webhook。WHK-03）を OpenAPI 3.1 の <c>webhooks</c> 節に載せる。
    /// 形は <see cref="WebhookContract"/>（送るときの書き方そのもの）から作る。画面の JSON の設定（列挙は数値）から
    /// 作らないのは、Webhook の本文は列挙を名前で送るため
    /// </summary>
    private static void AddWebhooks(OpenApiDocument document)
    {
        document.Webhooks ??= new Dictionary<string, IOpenApiPathItem>();
        var envelope = WebhookContract.Envelope;
        foreach (var (eventType, fields) in WebhookContract.Describe())
        {
            var properties = envelope.ToDictionary(f => f.Name, f => (IOpenApiSchema)SchemaOf(f));
            properties["type"] = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [JsonValue.Create(eventType)!] };
            properties["data"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = fields.ToDictionary(f => f.Name, f => (IOpenApiSchema)SchemaOf(f)),
                // null になり得る項目も、項目自体は必ず送る
                Required = fields.Select(f => f.Name).ToHashSet(),
            };
            document.Webhooks[eventType] = new OpenApiPathItem
            {
                Operations = new Dictionary<HttpMethod, OpenApiOperation>
                {
                    [HttpMethod.Post] = new OpenApiOperation
                    {
                        Summary = eventType,
                        Description =
                            "送信先として登録した URL へ POST する（管理 → Webhook）。ヘッダ X-Mes-Event（種類）・X-Mes-Delivery（送信の ID。再送でも同じ）・" +
                            "X-Mes-Timestamp（Unix 秒）・X-Mes-Signature（sha256= に続けて、秘密の値を鍵にした「X-Mes-Timestamp.本文」の HMAC-SHA256 の小文字16進）を付ける。" +
                            "少なくとも1回届け、順序は保証しないので、id で重複を捨てる。2xx を返せば送信済み。正しい今の値は連携API のフィードで取りに行く。",
                        RequestBody = new OpenApiRequestBody
                        {
                            Required = true,
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/json"] = new OpenApiMediaType
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Type = JsonSchemaType.Object,
                                        Properties = properties,
                                        Required = properties.Keys.ToHashSet(),
                                    },
                                },
                            },
                        },
                        Responses = new OpenApiResponses
                        {
                            ["2XX"] = new OpenApiResponse { Description = "受け取った（以後は送らない）" },
                        },
                    },
                },
            };
        }
    }

    private static OpenApiSchema SchemaOf(WebhookContract.Field field)
    {
        var schema = field.Kind switch
        {
            "integer" => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" },
            "number" => new OpenApiSchema { Type = JsonSchemaType.Number },
            "boolean" => new OpenApiSchema { Type = JsonSchemaType.Boolean },
            "date-time" => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" },
            "uuid" => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            "enum" => new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Enum = [.. field.Values!.Select(v => (JsonNode)JsonValue.Create(v)!)],
            },
            _ => new OpenApiSchema { Type = JsonSchemaType.String },
        };
        if (field.Nullable)
        {
            schema.Type |= JsonSchemaType.Null;
            schema.Enum?.Add(null!);
        }
        return schema;
    }
}
