using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MesApp.Api.Tests;

/// <summary>
/// DBプロバイダー切替（Spec.md 4章）。DBサーバーに接続せず、モデルとマイグレーションだけで確かめられることを見る。
/// マイグレーションはプロバイダーごとに別物なので、スキーマを変えたときに1つだけ作り忘れるのをここで止める。
/// </summary>
public class DatabaseProviderTests
{
    // 接続はしない（モデルの構築とマイグレーションの読み込みだけ）ので、形式として正しければよい
    private static readonly Dictionary<string, string> DummyConnectionStrings = new()
    {
        [DatabaseProviders.Sqlite] = "Data Source=:memory:",
        [DatabaseProviders.PostgreSql] = "Host=localhost;Database=mesapp;Username=mesapp;Password=dummy",
        [DatabaseProviders.SqlServer] = "Server=localhost;Database=mesapp;User Id=mesapp;Password=dummy;TrustServerCertificate=True",
    };

    public static TheoryData<string> Providers => new(DatabaseProviders.All);

    private static MesAppDbContext Create(string provider)
    {
        var builder = new DbContextOptionsBuilder<MesAppDbContext>();
        DependencyInjection.UseProvider(builder, new DatabaseOptions
        {
            Provider = provider,
            ConnectionString = DummyConnectionStrings[provider],
        });
        return new MesAppDbContext(builder.Options);
    }

    private static IRelationalModel RelationalModel(MesAppDbContext db) =>
        db.GetService<IDesignTimeModel>().Model.GetRelationalModel();

    [Theory]
    [MemberData(nameof(Providers))]
    public void マイグレーションがモデルに追いついている(string provider)
    {
        using var db = Create(provider);

        Assert.NotEmpty(db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges(),
            $"{provider} のマイグレーションが現在のモデルと一致しません。" +
            "スキーマを変えたときは3プロバイダーすべてでマイグレーションを追加してください（CLAUDE.md「コマンド」）。");
    }

    [Fact]
    public void 不明なプロバイダーは起動時に分かる()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DependencyInjection.UseProvider(new DbContextOptionsBuilder(), new DatabaseOptions { Provider = "MySql" }));
        Assert.Contains("MySql", ex.Message);
    }

    /// <summary>
    /// SQL Serverは nvarchar(max) の列を索引・キーに使えない。マイグレーションの作成時には分からず、
    /// 実DBへの適用で初めて失敗するため、ここで先に見つける
    /// </summary>
    [Fact]
    public void SQLServerで索引とキーに長さ無制限の文字列列を使っていない()
    {
        using var db = Create(DatabaseProviders.SqlServer);

        var violations = RelationalModel(db).Tables
            .SelectMany(t => t.Indexes.Select(i => (Table: t.Name, Name: i.Name, i.Columns))
                .Concat(t.UniqueConstraints.Select(u => (Table: t.Name, Name: u.Name, u.Columns)))
                .Concat(t.ForeignKeyConstraints.Select(f => (Table: t.Name, Name: f.Name, f.Columns))))
            .SelectMany(x => x.Columns
                .Where(c => c.StoreType.Contains("(max)", StringComparison.OrdinalIgnoreCase))
                .Select(c => $"{x.Table}.{c.Name}（{x.Name}）"))
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, "長さの指定（HasMaxLength）が必要な列: " + string.Join(", ", violations));
    }

    /// <summary>
    /// SQL Serverは、削除の連鎖（CASCADE / SET NULL）が同じ表へ複数の経路で届く形や循環を許さない。
    /// これも実DBへの適用で初めて失敗するため、外部キーの連鎖をたどって先に見つける
    /// </summary>
    [Fact]
    public void SQLServerで削除の連鎖が同じ表へ複数経路で届かない()
    {
        using var db = Create(DatabaseProviders.SqlServer);

        var edges = RelationalModel(db).Tables
            .SelectMany(t => t.ForeignKeyConstraints)
            .Where(f => f.OnDeleteAction is ReferentialAction.Cascade or ReferentialAction.SetNull or ReferentialAction.SetDefault)
            .ToLookup(f => f.PrincipalTable.Name, f => (Dependent: f.Table.Name, f.Name));

        var violations = new List<string>();
        foreach (var root in edges.Select(g => g.Key))
        {
            // root から連鎖でたどれる経路を数える。同じ表へ2本目の経路が来たら違反
            var reached = new Dictionary<string, string>();
            var stack = new Stack<(string Table, string Path)>([(root, root)]);
            while (stack.Count > 0)
            {
                var (table, path) = stack.Pop();
                foreach (var (dependent, fk) in edges[table])
                {
                    var next = $"{path} -[{fk}]-> {dependent}";
                    if (dependent == root)
                    {
                        violations.Add($"循環: {next}");
                        continue;
                    }
                    if (reached.TryGetValue(dependent, out var first))
                    {
                        violations.Add($"{first}  /  {next}");
                        continue;
                    }
                    reached[dependent] = next;
                    stack.Push((dependent, next));
                }
            }
        }

        Assert.True(violations.Count == 0,
            "SQL Serverで適用できない削除の連鎖があります（片方を NoAction にする）:\n" + string.Join("\n", violations.Distinct()));
    }
}
