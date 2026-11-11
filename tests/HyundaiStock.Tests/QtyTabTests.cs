using System.Net;
using System.Text.RegularExpressions;
using HyundaiStock.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

public sealed class QtyTabTests
{
    [Fact]
    public async Task Quantity_tab_is_shown_only_to_the_main_admin()
    {
        using var factory = new ApiFactory { Demo = true };
        using var main = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var super = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(main, DemoSeeder.MainPhone);
        await Login(super, "07790000006");

        var mainPage = await main.GetAsync("/Requests");
        Assert.Equal(HttpStatusCode.OK, mainPage.StatusCode);
        Assert.Contains("تصحيح الكمية", await mainPage.Content.ReadAsStringAsync());
        Assert.Contains("تعديل الكمية", await main.GetStringAsync("/Parts"));

        var denied = await super.GetAsync("/Requests");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.DoesNotContain("تصحيح الكمية", await denied.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await super.GetAsync("/Requests/Create")).StatusCode);

        var recount = await super.GetStringAsync("/Requests?kind=recount");
        Assert.DoesNotContain(">تصحيح الكمية<", recount);
        Assert.Contains("إعادة العد", recount);
        Assert.DoesNotContain("تعديل الكمية", await super.GetStringAsync("/Parts"));

        await using var scope = factory.Services.CreateAsyncScope();
        var partId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().InventoryParts.Select(x => x.Id).FirstAsync();
        Assert.DoesNotContain("طلبات تصحيح الكمية", await super.GetStringAsync($"/Parts/Details/{partId}?tab=corrections"));
        Assert.Contains("طلبات تصحيح الكمية", await main.GetStringAsync($"/Parts/Details/{partId}"));
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
