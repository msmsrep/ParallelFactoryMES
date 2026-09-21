using MesApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Audit;
using MesApp.Core.Contracts.Common;
using MesApp.Core.Contracts.Inventory;
using MesApp.Core.Contracts.Masters;
using MesApp.Core.Entities;

namespace MesApp.Api.Tests;

/// <summary>
/// 監査ログの参照（Spec.md 7.6）。記録するだけでは追跡できないため、
/// 引けること・管理者以外に見せないこと・書き換えられないことを確認する。
/// </summary>
public class AuditLogTests
{
    [Fact]
    public async Task 監査ログを絞り込んで参照できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        var created = await admin.PostAsJsonAsync("/api/locations",
            new LocationRequest("AUD-L-01", LocationAreaType.MaterialWarehouse, null));
        created.EnsureSuccessStatusCode();
        var location = await created.Content.ReadFromJsonAsync<LocationResponse>();
        await admin.PutAsJsonAsync($"/api/locations/{location!.Id}",
            new LocationRequest("AUD-L-01", LocationAreaType.MaterialWarehouse, "A-1"));

        // 絞り込みなし：ログイン等も含めて記録が返る
        var all = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>("/api/audit-logs");
        Assert.True(all!.Total > 0);
        // 新しい順（Spec.md 7.5：並べ替えを確定させる）
        Assert.True(all.Items[0].Timestamp >= all.Items[^1].Timestamp);

        // 対象で引く：このロケーションに何が起きたかを追える
        var byTarget = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/audit-logs?targetType={nameof(Location)}&targetId={location.Id}");
        Assert.Equal(2, byTarget!.Total);
        Assert.Equal(["Update", "Create"], byTarget.Items.Select(a => a.Action));
        Assert.All(byTarget.Items, a => Assert.Equal("Master", a.Category));
        Assert.All(byTarget.Items, a => Assert.Equal(TestAuth.AdminUser, a.UserName));

        // 分類・操作で引く
        var byAction = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            "/api/audit-logs?category=Master&action=Create");
        Assert.Equal(1, byAction!.Total);

        // 期間で引く：SQLiteでも記録時刻の比較が効くこと
        // 記録日（RecordedOn）はサーバーのローカル日付（Spec.md 7.6）。UTCの日付で組み立てると
        // JSTの00:00〜09:00に実行したときだけ1日ずれて0件になる
        var today = DateOnly.FromDateTime(DateTime.Now);
        var inRange = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/audit-logs?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(all.Total, inRange!.Total);

        var future = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/audit-logs?from={today.AddDays(1):yyyy-MM-dd}");
        Assert.Equal(0, future!.Total);

        var past = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/audit-logs?to={today.AddDays(-1):yyyy-MM-dd}");
        Assert.Equal(0, past!.Total);
    }

    [Fact]
    public async Task 監査ログの参照はシステム管理者だけができる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "pm1", "Passw0rd123", MesRoles.ProductionManager);

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/audit-logs/categories")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit-logs")).StatusCode);
    }

    [Fact]
    public async Task 監査ログは書き換えられない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        // 記録を書き換えられると監査の意味がなくなるので、参照以外の口を持たない
        var post = await admin.PostAsJsonAsync("/api/audit-logs", new { });
        var delete = await admin.DeleteAsync("/api/audit-logs/1");
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task 記録がある分類と操作を候補として返す()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        await admin.PostAsJsonAsync("/api/locations",
            new LocationRequest("AUD-L-02", LocationAreaType.MaterialWarehouse, null));

        var options = await admin.GetFromJsonAsync<List<AuditCategoryOption>>("/api/audit-logs/categories");

        var master = Assert.Single(options!, o => o is { Category: "Master", Action: "Create" });
        Assert.Equal(1, master.Count);
        Assert.Contains(options!, o => o.Category == "Auth");
    }

    [Fact]
    public async Task 取消は誰が何を取り消したか監査ログに残る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);

        var shipping = await (await admin.PostAsJsonAsync("/api/shipping-orders",
            new ShippingOrderCreateRequest("出荷先A", null, [new(ctx.ProductId, 1m)])))
            .Content.ReadFromJsonAsync<ShippingOrderResponse>();
        var canceled = await admin.PostAsync($"/api/shipping-orders/{shipping!.Id}/cancel", null);
        canceled.EnsureSuccessStatusCode();

        // 取消は「誰がなぜ消したか」を後から説明する必要がある操作（Spec.md 7.6）
        var logs = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/audit-logs?targetType={nameof(ShippingOrder)}&targetId={shipping.Id}&action=ShippingCancel");
        var log = Assert.Single(logs!.Items);
        Assert.Equal(TestAuth.AdminUser, log.UserName);
        Assert.Contains(shipping.ShippingNo, log.Detail);
        Assert.Contains("Instructed", log.Detail);
        Assert.Contains("Canceled", log.Detail);
    }

    [Fact]
    public async Task 棚卸の実棚数登録は上書き前の値も残る()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var ctx = await Phase3TestData.SetupAsync(admin);
        await Phase3TestData.ReceiveAsync(admin, ctx.MaterialId, 100m, ctx.MaterialLocationId);

        var stocktake = await (await admin.PostAsJsonAsync("/api/stocktakes",
            new StocktakeCreateRequest(ctx.MaterialLocationId)))
            .Content.ReadFromJsonAsync<StocktakeResponse>();
        var lineId = stocktake!.Lines[0].Id;

        await admin.PutAsJsonAsync($"/api/stocktakes/{stocktake.Id}/counts",
            new StocktakeCountRequest([new(lineId, 95m)]));
        await admin.PutAsJsonAsync($"/api/stocktakes/{stocktake.Id}/counts",
            new StocktakeCountRequest([new(lineId, 90m)]));

        // 実棚数は差異調整（＝在庫の増減）の根拠なので、書き換えの経緯が追えること
        var logs = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/audit-logs?targetType={nameof(Stocktake)}&targetId={stocktake.Id}&action=StocktakeCount");
        Assert.Equal(2, logs!.Total);
        Assert.Contains("95", logs.Items[0].Detail);  // 新しい順：2回目の記録に上書き前の95が入る
        Assert.Contains("90", logs.Items[0].Detail);
    }

    [Fact]
    public async Task 列の追加前からある記録にも記録日が埋まる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        // RecordedOn を持たなかった頃の行を再現する（既定値のまま）
        var moment = new DateTimeOffset(2026, 9, 2, 23, 30, 0, TimeSpan.Zero);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            if (!db.Database.IsSqlite())
            {
                // 埋め戻しはSQLiteの既存DBだけが対象（他のプロバイダーは列の追加後に作られるため過去行が無い）
                return;
            }
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO AuditLogs (Timestamp, RecordedOn, Category, Action, TargetType, TargetId)
                VALUES ({0}, '0001-01-01', 'Master', 'Legacy', 'Location', '999')
                """.Replace("{0}", $"'{moment:yyyy-MM-dd HH:mm:ss.fffffff}+00:00'"));
        }

        await factory.Services.InitializeDatabaseAsync();

        // 埋め戻した記録日で引ける（画面の表示と同じローカル日付基準）
        var expected = DateOnly.FromDateTime(moment.LocalDateTime);
        var logs = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>(
            $"/api/audit-logs?action=Legacy&from={expected:yyyy-MM-dd}&to={expected:yyyy-MM-dd}");
        Assert.Equal(1, logs!.Total);
    }

    [Theory]
    // この2つは26時間離れているため、どちらの暦日も**常に**食い違う。
    // サーバーのローカル日付で記録していると、少なくとも片方は必ずずれて落ちる
    // （1つのタイムゾーンで試すと、たまたま同じ暦日になる時間帯に通ってしまう）
    [InlineData("Pacific/Kiritimati")]
    [InlineData("Etc/GMT+12")]
    public async Task 記録日と保持期間の境界は工場のタイムゾーンで決まる(string timeZoneId)
    {
        // サーバーのローカルタイムで日付を取ると、UTCのコンテナに置いたときだけ
        // 記録日が現場と何時間もずれ、期間の絞り込みと保持期間の判定が見え方と食い違う
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        using var factory = new ApiFactory(new Dictionary<string, string>
        {
            ["BusinessDay:TimeZone"] = timeZoneId,
            // 境界時刻は記録日に効かない（製造日ではなく暦日）ことも同時に確認する
            ["BusinessDay:BoundaryHour"] = "6",
            ["Audit:RetentionYears"] = "0",
        });

        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        (await admin.PostAsJsonAsync("/api/locations",
            new LocationRequest("AUD-TZ-01", LocationAreaType.MaterialWarehouse, null)))
            .EnsureSuccessStatusCode();

        DateOnly recordedOn;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
            var log = await db.AuditLogs.AsNoTracking()
                .FirstAsync(a => a.Action == "Create" && a.TargetType == "Location");
            // 記録そのものの時刻から期待値を出すので、実行した瞬間に依存しない
            recordedOn = log.RecordedOn;
            Assert.Equal(ToLocalDate(log.Timestamp), recordedOn);
        }

        // 保持期間の境界も同じ基準で求まる。保持0年なら「工場の昨日」以前が削除対象
        var purged = await admin.PostAsJsonAsync("/api/audit-logs/purge?dryRun=true",
            new AuditLogPurgeRequest(recordedOn.AddDays(-1), "境界の確認"));
        purged.EnsureSuccessStatusCode();
        var result = (await purged.Content.ReadFromJsonAsync<AuditLogPurgeResult>())!;
        // 記録の直後に求めた境界なので、日付をまたいだ場合だけ1日進む
        Assert.True(
            result.RetentionCutoff == recordedOn.AddDays(-1)
            || result.RetentionCutoff == recordedOn,
            $"保持期間の境界 {result.RetentionCutoff} が記録日 {recordedOn} の前日と一致しない");

        DateOnly ToLocalDate(DateTimeOffset moment) =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, timeZone).DateTime);
    }

    [Fact]
    public async Task 保持期間を過ぎた監査ログだけ一括削除できる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);

        // 保持期間（既定5年）より古い記録を用意する
        var old = DateOnly.FromDateTime(DateTime.Now).AddYears(-6);
        await InsertLegacyLogAsync(factory, old, "Old1");
        await InsertLegacyLogAsync(factory, old, "Old2");
        var beforeTotal = (await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>("/api/audit-logs"))!.Total;

        // dryRun：件数だけ確認し、DBには触らない
        var preview = await (await admin.PostAsJsonAsync("/api/audit-logs/purge?dryRun=true",
            new AuditLogPurgeRequest(old, "容量削減のため")))
            .Content.ReadFromJsonAsync<AuditLogPurgeResult>();
        Assert.Equal(2, preview!.DeletedCount);
        Assert.True(preview.DryRun);
        Assert.Equal(beforeTotal,
            (await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>("/api/audit-logs"))!.Total);

        // 実行
        var purged = await (await admin.PostAsJsonAsync("/api/audit-logs/purge",
            new AuditLogPurgeRequest(old, "容量削減のため")))
            .Content.ReadFromJsonAsync<AuditLogPurgeResult>();
        Assert.Equal(2, purged!.DeletedCount);
        Assert.False(purged.DryRun);

        // 古い分だけ消え、新しい分は残る。削除したこと自体は記録に残る（＝差引で1件増える）
        var after = await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>("/api/audit-logs");
        Assert.Equal(beforeTotal - 2 + 1, after!.Total);
        var purgeLog = Assert.Single(after.Items, a => a is { Category: "Audit", Action: "Purge" });
        Assert.Contains("容量削減のため", purgeLog.Detail);
        Assert.Contains("\"deleted\":2", purgeLog.Detail);
        Assert.Equal(TestAuth.AdminUser, purgeLog.UserName);
    }

    [Fact]
    public async Task 保持期間内の監査ログは削除できない()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        var before = (await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>("/api/audit-logs"))!.Total;

        // 直前の操作の記録を消して隠せてしまうため、保持期間の内側は消させない
        var today = DateOnly.FromDateTime(DateTime.Now);
        var refused = await admin.PostAsJsonAsync("/api/audit-logs/purge",
            new AuditLogPurgeRequest(today, "都合が悪いので"));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        // 理由なしも受け付けない
        var noReason = await admin.PostAsJsonAsync("/api/audit-logs/purge",
            new AuditLogPurgeRequest(today.AddYears(-6), ""));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        Assert.Equal(before,
            (await admin.GetFromJsonAsync<PagedResult<AuditLogResponse>>("/api/audit-logs"))!.Total);
    }

    [Fact]
    public async Task 一括削除はシステム管理者だけができる()
    {
        using var factory = new ApiFactory();
        using var admin = await TestAuth.CreateAdminClientAsync(factory);
        using var manager = await TestAuth.CreateUserClientAsync(
            factory, admin, "pm2", "Passw0rd123", MesRoles.ProductionManager);

        var response = await manager.PostAsJsonAsync("/api/audit-logs/purge",
            new AuditLogPurgeRequest(DateOnly.FromDateTime(DateTime.Now).AddYears(-6), "試し"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>保持期間より古い監査ログを直接入れる（時間を巻き戻せないため）</summary>
    private static async Task InsertLegacyLogAsync(ApiFactory factory, DateOnly recordedOn, string action)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MesAppDbContext>();
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp = new DateTimeOffset(recordedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            RecordedOn = recordedOn,
            Category = "Master",
            Action = action,
        });
        await db.SaveChangesAsync();
    }
}
