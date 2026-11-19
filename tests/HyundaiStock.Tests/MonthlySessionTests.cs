using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

/// <summary>Monthly counts beside the annual one: same work, separate sessions, one open at a time, named everywhere.</summary>
public sealed class MonthlySessionTests
{
    private static MemoryStream Workbook(string part)
    {
        using var wb = new XLWorkbook(); var sheet = wb.AddWorksheet("Not Counted Parts");
        var headers = new[] { "Part Number", "System Quantity", "Branch" };
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Cell(2, 1).Value = part; sheet.Cell(2, 2).Value = 4; sheet.Cell(2, 3).Value = "904";
        var stream = new MemoryStream(); wb.SaveAs(stream); stream.Position = 0; return stream;
    }

    [Fact]
    public async Task Monthly_session_follows_the_same_rules_and_is_named()
    {
        using var factory = new ApiFactory { Demo = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<SessionService>();
        var main = await db.Admins.SingleAsync(x => x.Role == AdminRole.MainAdmin);
        var me = new AdminScope(main.Id, main.Name, main.Role, null);
        var annual = await db.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress);
        Assert.Equal(SessionKind.Annual, annual.Kind);
        var annualCount = await db.Sessions.CountAsync(x => x.Kind == SessionKind.Annual);
        Assert.Equal($"جرد سنوي {annual.Year}", annual.Name);
        await db.Branches.Where(x => x.Number == 904).ExecuteUpdateAsync(u => u.SetProperty(x => x.Name, "البصرة"));

        // Only one session open at a time, annual or monthly.
        using (var f = Workbook("MON-1"))
            Assert.Contains("قيد التنفيذ", (await svc.StartAsync(SessionKind.Monthly, annual.Year, 2, new[] { ((Stream)f, "m.xlsx", f.Length) }, me)).Error);
        Assert.True((await svc.EndAsync(annual.Id, me)).Ok);

        // A month is required and must be 1 to 12.
        using (var f = Workbook("MON-1"))
            Assert.Equal("اختر شهر الجرد", (await svc.StartAsync(SessionKind.Monthly, annual.Year, null, new[] { ((Stream)f, "m.xlsx", f.Length) }, me)).Error);
        using (var f = Workbook("MON-1"))
            Assert.False((await svc.StartAsync(SessionKind.Monthly, annual.Year, 13, new[] { ((Stream)f, "m.xlsx", f.Length) }, me)).Ok);

        // The same year already has its annual session, yet a monthly one starts.
        using (var f = Workbook("MON-1"))
        {
            var started = await svc.StartAsync(SessionKind.Monthly, annual.Year, 2, new[] { ((Stream)f, "m.xlsx", f.Length) }, me);
            Assert.True(started.Ok, started.Error);
        }
        var monthly = await db.Sessions.SingleAsync(x => x.Kind == SessionKind.Monthly);
        Assert.Equal(2, monthly.Month);
        Assert.Equal($"جرد شباط {annual.Year}", monthly.Name);
        Assert.Equal(SessionStatus.InProgress, monthly.Status);
        Assert.Equal(1, await db.InventoryParts.CountAsync(x => x.SessionId == monthly.Id));

        // The mobile API now counts against the open monthly session and names it.
        var mobile = scope.ServiceProvider.GetRequiredService<MobileService>();
        var current = await mobile.CurrentSessionAsync();
        Assert.Equal(monthly.Id, current!.Id);
        Assert.Equal(monthly.Name, current.Name);

        // One monthly session per month, even a finished one. The annual year stays unique.
        Assert.True((await svc.EndAsync(monthly.Id, me)).Ok);
        using (var f = Workbook("MON-2"))
        {
            var dup = await svc.StartAsync(SessionKind.Monthly, annual.Year, 2, new[] { ((Stream)f, "m.xlsx", f.Length) }, me);
            Assert.False(dup.Ok); Assert.Contains("للشهر نفسه", dup.Error);
        }
        using (var f = Workbook("MON-3"))
            Assert.True((await svc.StartAsync(SessionKind.Monthly, annual.Year, 3, new[] { ((Stream)f, "m.xlsx", f.Length) }, me)).Ok);
        // Monthly sessions never touch the annual ones.
        Assert.Equal(annualCount, await db.Sessions.CountAsync(x => x.Kind == SessionKind.Annual));
        Assert.Equal(2, await db.Sessions.CountAsync(x => x.Kind == SessionKind.Monthly));
    }

    [Fact]
    public async Task Lists_and_finance_filter_by_session_so_each_month_has_its_own_count()
    {
        using var factory = new ApiFactory { Demo = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = scope.ServiceProvider.GetRequiredService<SessionService>();
        var parts = scope.ServiceProvider.GetRequiredService<PartService>();
        var main = await db.Admins.SingleAsync(x => x.Role == AdminRole.MainAdmin);
        var me = new AdminScope(main.Id, main.Name, main.Role, null);
        var annual = await db.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress);
        var annualParts = await db.InventoryParts.CountAsync(x => x.SessionId == annual.Id && !db.NewPartRequests.Any(r => r.InventoryPartId == x.Id));
        Assert.True((await svc.EndAsync(annual.Id, me)).Ok);
        using (var f = Workbook("MON-ONLY"))
            Assert.True((await svc.StartAsync(SessionKind.Monthly, annual.Year, 5, new[] { ((Stream)f, "m.xlsx", f.Length) }, me)).Ok);
        var monthly = await db.Sessions.SingleAsync(x => x.Kind == SessionKind.Monthly);

        var (monthPage, monthSession) = await parts.ListAsync(new PageQuery(1, 50), me, null, monthly.Id);
        Assert.Equal(monthly.Id, monthSession!.Id);
        Assert.Equal("MON-ONLY", Assert.Single(monthPage.Items).PartNumber);
        var (annualPage, _) = await parts.ListAsync(new PageQuery(1, 50), me, null, annual.Id);
        Assert.Equal(annualParts, annualPage.TotalCount);
        // No filter: the latest session, which is the monthly one now open.
        var (latest, _) = await parts.ListAsync(new PageQuery(1, 50), me, null, null);
        Assert.Equal("MON-ONLY", Assert.Single(latest.Items).PartNumber);

        var lookup = scope.ServiceProvider.GetRequiredService<SessionLookup>();
        var names = (await lookup.OptionsAsync()).Select(x => x.Name).ToList();
        Assert.Contains($"جرد سنوي {annual.Year}", names);
        Assert.Contains($"جرد أيار {annual.Year}", names);

        var export = scope.ServiceProvider.GetRequiredService<ExportService>();
        var (file, error, forbidden) = await export.FinanceFileAsync(new FinanceFilter { SessionId = monthly.Id }, me);
        Assert.False(forbidden); Assert.Null(error);
        using var wb = new XLWorkbook(new MemoryStream(file!.Content));
        var ws = wb.Worksheets.First();
        var row = Enumerable.Range(1, ws.LastColumnUsed()!.ColumnNumber()).Select(c => ws.Cell(2, c).GetString()).ToList();
        Assert.Equal(2, ws.LastRowUsed()!.RowNumber());
        Assert.Contains("MON-ONLY", row);
        Assert.Contains($"جرد أيار {annual.Year}", row);
    }

    [Fact]
    public async Task Sessions_page_offers_annual_and_monthly_and_pages_use_the_session_filter()
    {
        using var factory = new ApiFactory { Demo = true };
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["PhoneNumber"] = DemoSeeder.MainPhone, ["Password"] = "Test1234", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var sessions = await client.GetStringAsync("/Sessions");
        Assert.Contains("name=\"kind\"", sessions);
        Assert.Contains("جرد شهري", sessions);
        Assert.Contains("name=\"month\"", sessions);
        Assert.Contains("name=\"year\"", sessions);
        Assert.Contains("جرد سنوي", sessions);

        foreach (var page in new[] { "/Parts", "/Cars", "/Cars?view=missing", "/Requests", "/RecountAsks", "/NewParts", "/ApprovedParts", "/" })
        {
            var body = await client.GetStringAsync(page);
            Assert.Contains("name=\"sessionId\"", body);
            Assert.DoesNotContain("name=\"year\"", body);
        }
        Assert.Contains("جلسة الجرد", await client.GetStringAsync("/Parts"));
    }
}
