using System.Reflection;
using System.Text.Json;
using MesApp.Core.Contracts.Integration;

namespace MesApp.Api.Services.Webhooks;

/// <summary>
/// Webhook の本文の形（Spec.md 3.8 Webhook。WHK-03）を、送るときの書き方（<see cref="WebhookPublisher.PayloadJson"/>：
/// camelCase・列挙は名前）のとおりに書き出す。OpenAPI 文書の <c>webhooks</c> 節と、
/// 約束の基準ファイル（<c>tests/MesApp.Api.Tests/WebhookContract.v1.json</c>）の両方がこれを使う
/// （別々に書くと、文書と実際に送る形がずれる）
/// </summary>
public static class WebhookContract
{
    /// <summary>項目の形。<paramref name="Kind"/> は string・integer・number・boolean・date-time・uuid・enum</summary>
    /// <param name="Values">列挙の値の名前（enum のときだけ）</param>
    public sealed record Field(string Name, string Kind, bool Nullable, List<string>? Values);

    /// <summary>種類ごとの本文の data の形（種類の名前の順）</summary>
    public static IReadOnlyList<(string EventType, List<Field> Fields)> Describe() =>
        WebhookPayloads.DataTypes
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (p.Key, Describe(p.Value)))
            .ToList();

    /// <summary>本文の共通の包み（data 以外）。受け側との約束なので、これも基準ファイルに固定する</summary>
    public static List<Field> Envelope { get; } =
    [
        new("id", "uuid", false, null),
        new("sequence", "integer", false, null),
        new("type", "string", false, null),
        new("version", "integer", false, null),
        new("occurredAt", "date-time", false, null),
        new("summary", "string", false, null),
    ];

    private static List<Field> Describe(Type type)
    {
        var nullability = new NullabilityInfoContext();
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is not null && p.Name != "EqualityContract")
            .Select(p =>
            {
                var underlying = Nullable.GetUnderlyingType(p.PropertyType);
                var valueType = underlying ?? p.PropertyType;
                var nullable = underlying is not null
                    || (!p.PropertyType.IsValueType && nullability.Create(p).ReadState == NullabilityState.Nullable);
                var name = JsonNamingPolicy.CamelCase.ConvertName(p.Name);
                return valueType.IsEnum
                    ? new Field(name, "enum", nullable, [.. Enum.GetNames(valueType)])
                    : new Field(name, KindOf(valueType), nullable, null);
            })
            .ToList();
    }

    private static string KindOf(Type type) =>
        type == typeof(string) ? "string"
        : type == typeof(int) || type == typeof(long) ? "integer"
        : type == typeof(decimal) || type == typeof(double) ? "number"
        : type == typeof(bool) ? "boolean"
        : type == typeof(DateTimeOffset) || type == typeof(DateTime) ? "date-time"
        : type == typeof(Guid) ? "uuid"
        // 約束に入れられる型を増やすときは、ここと OpenAPI の書き出し（IntegrationOpenApi）を揃える
        : throw new NotSupportedException($"Webhook の本文に使えない型です: {type.Name}");
}
