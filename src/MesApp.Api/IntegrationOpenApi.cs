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
                return Task.CompletedTask;
            });
        });
}
