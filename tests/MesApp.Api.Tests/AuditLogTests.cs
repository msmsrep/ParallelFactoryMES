using System.Net;
using System.Net.Http.Json;
using MesApp.Core.Constants;
using MesApp.Core.Contracts.Audit;
using MesApp.Core.Contracts.Common;
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
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
}
