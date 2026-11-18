using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

/// <summary>Quantity-correction requests are gone from the dashboard and the mobile API. Count type is exposed in `me` and enforced.</summary>
public sealed class QuantityRemovalTests
{
    [Fact]
    public async Task Quantity_correction_pages_and_endpoints_no_longer_exist()
    {
        using var factory = new ApiFactory { Demo = true };
        using var main = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(main, DemoSeeder.MainPhone);

        var requests = await main.GetStringAsync("/Requests");
        Assert.Contains("مهام إعادة العد", requests);
        Assert.DoesNotContain("تصحيح الكمية", requests);
        Assert.DoesNotContain("تعديل الكمية", await main.GetStringAsync("/Parts"));
        Assert.DoesNotContain("تصحيح الكمية", await main.GetStringAsync("/"));
        var form = await main.GetStringAsync("/Users/Create");
        Assert.Contains("نوع الجرد المسموح", form);
        Assert.Contains("name=\"CountType\"", form);
        Assert.Contains("نوع الجرد", await main.GetStringAsync("/Users"));
        await AssertGone(main, "/Requests/Create");

        await using var scope = factory.Services.CreateAsyncScope();
        var partId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().InventoryParts.Select(x => x.Id).FirstAsync();
        Assert.DoesNotContain("طلبات تصحيح الكمية", await main.GetStringAsync($"/Parts/Details/{partId}?tab=corrections"));
        await AssertGone(main, $"/Parts/QuantityForm/{partId}");

        var mobile = factory.CreateClient();
        await LiveReadinessTests.LoginMobile(mobile);
        Assert.Equal(HttpStatusCode.NotFound, (await mobile.GetAsync("/api/v1/qty-requests")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mobile.PostAsJsonAsync("/api/v1/qty-requests", new { partId, newQuantity = 3, reason = "x" })).StatusCode);
    }

    [Fact]
    public async Task Me_reports_the_count_type_and_the_api_rejects_a_disallowed_count()
    {
        using var factory = new ApiFactory { Demo = true };
        var mobile = factory.CreateClient();
        await LiveReadinessTests.LoginMobile(mobile);
        var me = (await mobile.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("data");
        Assert.Equal("both", me.GetProperty("countType").GetString());
        Assert.True(me.GetProperty("canCountParts").GetBoolean());
        Assert.True(me.GetProperty("canScanVins").GetBoolean());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.AppUsers.SingleAsync(x => x.PhoneNumber == DemoSeeder.CounterPhone);
        user.CountType = CountType.Parts;
        await db.SaveChangesAsync();
        me = (await mobile.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("data");
        Assert.Equal("parts", me.GetProperty("countType").GetString());
        Assert.False(me.GetProperty("canScanVins").GetBoolean());
        var wh = await db.AppUserWarehouses.Where(x => x.AppUserId == user.Id).Select(x => x.WarehouseId).FirstAsync();
        var scan = await mobile.PostAsJsonAsync("/api/v1/vins/scan", new { vin = DemoSeeder.Vin(1), warehouseId = wh, latitude = 33.3, longitude = 44.4 });
        Assert.Equal(HttpStatusCode.Forbidden, scan.StatusCode);
        var body = await scan.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("حسابك غير مخوّل بجرد السيارات", body.GetProperty("message").GetString());
    }

    /// <summary>Dashboard 404s redirect to the not-found page.</summary>
    private static async Task AssertGone(HttpClient client, string path)
    {
        var r = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal("/Home/NotFoundPage", r.Headers.Location!.OriginalString);
    }

    private static async Task Login(HttpClient client, string phone)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PhoneNumber"] = phone, ["Password"] = "Test1234", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }
}
