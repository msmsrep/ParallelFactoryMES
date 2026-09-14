using MesApp.Core.Entities;

namespace MesApp.Api.Policies;

/// <summary>
/// 作業区／資源階層（BOR）の親子関係の妥当性判定（Spec.md 5.1 WorkCenter。I-10-20-02）。
/// <para>
/// 工場・ライン・エリア・作業区を1つの自己参照ツリーで持つため、段の飛び越し・親のない下位段・
/// 循環参照のいずれもDBの制約だけでは防げない。判定を1か所に置き、単票API（<c>WorkCentersController</c>）と
/// マスタCSV取込（<c>MasterCsvService</c>）の両方から同じ条件で呼ぶ。経路ごとに条件を持たせると、
/// 片方だけ直したときにフォームからは作れない階層がCSVからは登録できてしまう（Spec.md 7.4）。
/// </para>
/// </summary>
public static class WorkCenterHierarchyPolicy
{
    /// <summary>この段の親に必要な段（工場は親を持たないためnull）</summary>
    public static WorkCenterLevel? RequiredParentLevel(WorkCenterLevel level) => level switch
    {
        WorkCenterLevel.Plant => null,
        WorkCenterLevel.Line => WorkCenterLevel.Plant,
        WorkCenterLevel.Area => WorkCenterLevel.Line,
        _ => WorkCenterLevel.Area,
    };

    /// <summary>
    /// 親子関係を検証する。問題があれば日本語の理由を返す（問題なければnull）。
    /// </summary>
    /// <param name="code">登録・更新しようとしている作業区のコード（メッセージ用）</param>
    /// <param name="level">登録・更新しようとしている段</param>
    /// <param name="parent">指定された親（未指定ならnull）</param>
    /// <param name="selfId">更新時は自分のId、新規作成時はnull</param>
    /// <param name="all">既存の全件（循環の検出に使う）</param>
    public static string? Check(
        string code,
        WorkCenterLevel level,
        WorkCenter? parent,
        int? selfId,
        IReadOnlyCollection<WorkCenter> all)
    {
        var required = RequiredParentLevel(level);

        if (required is null)
        {
            return parent is null
                ? null
                : $"'{code}' は工場のため、上位の資源を指定できません。";
        }

        if (parent is null)
        {
            return $"'{code}' には上位の資源（{LevelName(required.Value)}）の指定が必要です。";
        }

        if (parent.Level != required.Value)
        {
            return $"'{code}'（{LevelName(level)}）の上位には{LevelName(required.Value)}を指定してください" +
                   $"（'{parent.Code}' は{LevelName(parent.Level)}です）。";
        }

        // 循環：自分自身、または自分の配下を親にすると木が閉じる
        if (selfId is { } id)
        {
            if (parent.Id == id)
            {
                return $"'{code}' の上位に自分自身は指定できません。";
            }
            if (IsDescendantOf(parent, id, all))
            {
                return $"'{parent.Code}' は '{code}' の配下にあるため、上位に指定できません（循環します）。";
            }
        }

        return null;
    }

    /// <summary><paramref name="node"/> が <paramref name="ancestorId"/> の配下にあるか</summary>
    private static bool IsDescendantOf(WorkCenter node, int ancestorId, IReadOnlyCollection<WorkCenter> all)
    {
        var byId = all.ToDictionary(x => x.Id);
        var current = node;
        // 段は4つしかないが、既存データが壊れていても止まるように打ち切りを入れる
        for (var depth = 0; depth < 8; depth++)
        {
            if (current.ParentId is not { } parentId)
            {
                return false;
            }
            if (parentId == ancestorId)
            {
                return true;
            }
            if (!byId.TryGetValue(parentId, out var next))
            {
                return false;
            }
            current = next;
        }
        return false;
    }

    /// <summary>エラーメッセージ用の段の日本語名</summary>
    public static string LevelName(WorkCenterLevel level) => level switch
    {
        WorkCenterLevel.Plant => "工場",
        WorkCenterLevel.Line => "ライン",
        WorkCenterLevel.Area => "エリア",
        _ => "作業区",
    };
}
