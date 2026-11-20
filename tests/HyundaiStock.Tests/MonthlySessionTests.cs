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
        var me = new AdminScope(main.Id, main.Name, AdminRole.AccountsManager, null);
        var annual = await db.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress);
        Assert.Equal(SessionKind.Annual, annual.Kind);
        var annualCount = await db.Sessions.CountAsync(x => x.Kind == SessionKind.Annual);
        Assert.Equal($"جرد سنوي {annual.Year}", annual.Name);
        await db.Branches.Where(x => x.Number == 904).ExecuteUpdateAsync(u => u.SetProperty(x => x.Name, "البصرة"));

        // Only one session open at a time, annual or monthly.
        using (var f = Workbook("MON-1"))
            Assert.Contains("جلسة جرد مفتوحة حاليًا", (await svc.StartAsync(SessionKind.Monthly, annual.Year, 2, new[] { ((Stream)f, "m.xlsx", f.Length) }, me)).Error);
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
        var am = new AdminScope(main.Id, main.Name, AdminRole.AccountsManager, null);
        var annual = await db.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress);
        var annualParts = await db.InventoryParts.CountAsync(x => x.SessionId == annual.Id && !db.NewPartRequests.Any(r => r.InventoryPartId == x.Id));
        Assert.True((await svc.EndAsync(annual.Id, am)).Ok);
        using (var f = Workbook("MON-ONLY"))
            Assert.True((await svc.StartAsync(SessionKind.Monthly, annual.Year, 5, new[] { ((Stream)f, "m.xlsx", f.Length) }, am)).Ok);
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

/// <summary>Opening and closing a session: accounts manager only, never two open at once, and the refusal is shown.</summary>
public sealed class SessionRulesTests
{
    private static MemoryStream Workbook()
    {
        using var wb = new XLWorkbook(); var sheet = wb.AddWorksheet("Not Counted Parts");
        var headers = new[] { "Part Number", "System Quantity", "Branch" };
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Cell(2, 1).Value = "RULE-1"; sheet.Cell(2, 2).Value = 4; sheet.Cell(2, 3).Value = "904";
        var stream = new MemoryStream(); wb.SaveAs(stream); stream.Position = 0; return stream;
    }

    private static async Task<HttpClient> LoginAs(ApiFactory factory, string phone)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/Account/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["PhoneNumber"] = phone, ["Password"] = "Test1234", ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static string TokenOf(string html) =>
        WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private static async Task<(HttpStatusCode Status, string Page)> Start(HttpClient client, string kind, string year, string? month)
    {
        var form = await client.GetStringAsync("/Sessions");
        using var data = Workbook();
        using var body = new MultipartFormDataContent
        {
            { new StringContent(TokenOf(form)), "__RequestVerificationToken" }, { new StringContent(kind), "kind" }, { new StringContent(year), "year" },
            { new ByteArrayContent(data.ToArray()), "file", "export.xlsx" }
        };
        if (month != null) body.Add(new StringContent(month), "month");
        var response = await client.PostAsync("/Sessions/Start", body);
        var page = response.StatusCode == HttpStatusCode.Redirect ? await client.GetStringAsync(response.Headers.Location) : "";
        return (response.StatusCode, WebUtility.HtmlDecode(page));
    }

    [Fact]
    public async Task Opening_a_session_while_one_is_active_shows_a_clear_error_and_changes_nothing()
    {
        using var factory = new ApiFactory { Demo = true };
        using var accounts = await LoginAs(factory, "07790000012");
        int before;
        await using (var scope = factory.Services.CreateAsyncScope())
            before = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Sessions.CountAsync();

        // The Sessions page itself warns while a session is open.
        var list = WebUtility.HtmlDecode(await accounts.GetStringAsync("/Sessions"));
        Assert.Contains("توجد جلسة جرد مفتوحة", list);

        foreach (var (kind, month) in new[] { ("Annual", (string?)null), ("Monthly", "7") })
        {
            var (status, page) = await Start(accounts, kind, "2041", month);
            Assert.Equal(HttpStatusCode.Redirect, status);
            Assert.Contains("data-sticky", page);
            Assert.Contains("لا يمكن فتح جلسة جديدة: توجد جلسة جرد مفتوحة حاليًا", page);
        }
        await using var check = factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(before, await db.Sessions.CountAsync());
        Assert.Equal(1, await db.Sessions.CountAsync(x => x.Status == SessionStatus.InProgress));
    }

    [Fact]
    public async Task Only_the_accounts_manager_opens_and_closes_from_the_dashboard()
    {
        using var factory = new ApiFactory { Demo = true };
        int liveId;
        await using (var scope = factory.Services.CreateAsyncScope())
            liveId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Sessions.Where(x => x.Status == SessionStatus.InProgress).Select(x => x.Id).SingleAsync();

        // Main admin and super admin may view the sessions, but cannot open or close them. The accountant cannot even open the page.
        foreach (var phone in new[] { DemoSeeder.MainPhone, "07790000006" })
        {
            using var other = await LoginAs(factory, phone);
            var html = await other.GetStringAsync("/Sessions");
            Assert.DoesNotContain("data-modal=\"#start-m\"", html);
            Assert.DoesNotContain("/Sessions/End", html);
            var end = await other.PostAsync("/Sessions/End", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = liveId.ToString(), ["__RequestVerificationToken"] = TokenOf(html) }));
            Assert.Equal(HttpStatusCode.Forbidden, end.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Start(other, "Annual", "2042", null)).Status);
        }
        using (var accountant = await LoginAs(factory, "07790000005"))
            Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/Sessions")).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.Equal(SessionStatus.InProgress, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Sessions.AsNoTracking().SingleAsync(x => x.Id == liveId)).Status);

        using var accounts = await LoginAs(factory, "07790000012");
        var page = await accounts.GetStringAsync("/Sessions");
        Assert.Contains("data-modal=\"#start-m\"", page);
        Assert.Contains("/Sessions/End", page);
        var closed = await accounts.PostAsync("/Sessions/End", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = liveId.ToString(), ["__RequestVerificationToken"] = TokenOf(page) }));
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);
        await using var check = factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(SessionStatus.Completed, (await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == liveId)).Status);
        // Nothing is open now, so the accounts manager can open the next one.
        var (status, _) = await Start(accounts, "Monthly", DateTime.UtcNow.Year.ToString(), "9");
        Assert.Equal(HttpStatusCode.Redirect, status);
        Assert.Equal(1, await db.Sessions.CountAsync(x => x.Status == SessionStatus.InProgress));
    }

    [Fact]
    public async Task The_database_itself_refuses_a_second_open_session()
    {
        using var factory = new ApiFactory { Demo = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.Admins.Select(x => x.Id).FirstAsync();
        Assert.Equal(1, await db.Sessions.CountAsync(x => x.Status == SessionStatus.InProgress));
        db.Sessions.Add(new StocktakingSession { Year = 2090, Name = "جرد سنوي 2090", StartedByAdminId = adminId, FileName = "a.xlsx", StoredFileName = "a.xlsx" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        // A completed session is not blocked.
        db.Sessions.Add(new StocktakingSession { Year = 2091, Name = "جرد سنوي 2091", Status = SessionStatus.Completed, StartedByAdminId = adminId, FileName = "a.xlsx", StoredFileName = "a.xlsx" });
        await db.SaveChangesAsync();
    }
}
