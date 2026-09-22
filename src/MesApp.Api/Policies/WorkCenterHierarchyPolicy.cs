using MesApp.Core.Localization;
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
            return $"'{code}' には上位の資源（{EnumLabels.Of(required.Value)}）の指定が必要です。";
        }

        if (parent.Level != required.Value)
        {
            return $"'{code}'（{EnumLabels.Of(level)}）の上位には{EnumLabels.Of(required.Value)}を指定してください" +
                   $"（'{parent.Code}' は{EnumLabels.Of(parent.Level)}です）。";
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

    /// <summary>
    /// 設備の設置場所として指定できる資源か（Spec.md 5.1 Equipment）。指定できない場合は日本語の理由を返す。
    /// <para>
    /// 設備は最下段の作業区にだけ紐付ける。作業区が「作業の管理単位」であり、ここを一意に決めておかないと
    /// 同じ設備群がラインに付いたり作業区に付いたりして、工程別・作業区別の集計軸が定まらなくなるため。
    /// 上位（エリア・ライン・工場）は作業区から辿れば得られる。
    /// </para>
    /// </summary>
    public static string? CheckEquipmentPlacement(WorkCenter? workCenter)
    {
        if (workCenter is null)
        {
            return null;
        }
        if (workCenter.Level != WorkCenterLevel.WorkCenter)
        {
            return $"設備の設置場所には作業区を指定してください（'{workCenter.Code}' は{EnumLabels.Of(workCenter.Level)}です）。";
        }
        return workCenter.IsActive
            ? null
            : $"作業区 '{workCenter.Code}' は無効のため、設置場所に指定できません。";
    }

    /// <summary>
    /// 在庫ロケーションの所属先として指定できる資源か（Spec.md 5.1 Location）。
    /// <para>
    /// 部材倉庫・製品倉庫は工場直下に置かれることが実際に多いため、段は問わない。有効であることだけを求める。
    /// </para>
    /// </summary>
    public static string? CheckLocationPlacement(WorkCenter? workCenter)
    {
        if (workCenter is null)
        {
            return null;
        }
        return workCenter.IsActive
            ? null
            : $"作業区 '{workCenter.Code}' は無効のため、所属先に指定できません。";
    }

    /// <summary>
    /// 指定した資源とその配下すべてのIdを返す（進捗などを上位の段でまとめて見るため）。
    /// <para>
    /// 作業指示が持つ作業区は最下段なので、ラインや工場で絞り込むには配下へ展開する必要がある。
    /// 展開せずに「指定したIdと一致するもの」で絞ると、ラインを選んだときに常に0件になる。
    /// </para>
    /// </summary>
    public static HashSet<int> SelfAndDescendantIds(int rootId, IReadOnlyCollection<WorkCenter> all)
    {
        var childrenByParent = all
            .Where(x => x.ParentId is not null)
            .GroupBy(x => x.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new HashSet<int> { rootId };
        var queue = new Queue<int>([rootId]);
        while (queue.Count > 0)
        {
            if (!childrenByParent.TryGetValue(queue.Dequeue(), out var children))
            {
                continue;
            }
            foreach (var child in children.Where(c => result.Add(c.Id)))
            {
                queue.Enqueue(child.Id);
            }
        }
        return result;
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
}
