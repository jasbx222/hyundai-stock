using System.Net;
using ClosedXML.Excel;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

public sealed class SessionImportTests
{
    private static readonly Branch[] Branches = { new() { Id = 1, Number = 904, Name = "البصرة" }, new() { Id = 2, Number = 905, Name = "أربيل" } };

    private static MemoryStream Workbook(string[] headers, params object[][] rows)
    {
        using var wb = new XLWorkbook(); var sheet = wb.AddWorksheet("Not Counted Parts");
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++) sheet.Cell(r + 2, c + 1).Value = XLCellValue.FromObject(rows[r][c]);
        var stream = new MemoryStream(); wb.SaveAs(stream); stream.Position = 0; return stream;
    }

    [Theory]
    [InlineData("بصرة", 1)]
    [InlineData("أربيل", 2)]
    [InlineData("904", 1)]
    [InlineData("٩٠٤", 1)]
    public void Export_report_accepts_per_row_branch_names_or_text_codes(string branch, int expectedId)
    {
        using var stream = Workbook(new[] { "Part Number", "Description", "Location In Stock", "System Quantity", "Branch", "Counted Quantity", "User", "Difference" },
            new object[] { "PART-01", "فلتر", "A-1", 3, branch, 99, "export user", 96 });
        var result = new ExcelImportService().Read(stream, Array.Empty<Warehouse>(), Branches);
        Assert.Empty(result.Errors); var part = Assert.Single(result.Parts);
        Assert.Equal(expectedId, part.BranchId); Assert.Equal(3, part.SystemQuantity);
        Assert.Equal("A-1", part.SystemLocation); Assert.Null(part.CountedQuantity); Assert.Null(part.CountedByUserId);
    }

    [Fact]
    public void SessionImport_Vins_ReadsDealerAndManufactureYear()
    {
        using var stream = Workbook(new[] { "رقم الشاصي", "نوع المركبة", "الوكيل", "سنة الصنع" },
            new object[] { "TMAJB81D4TJ653724", "Tucson", "وكيل بغداد", 2025 },
            new object[] { "TMAJB81D4TJ653725", "Elantra", "", "" },
            new object[] { "TMAJB81D4TJ653726", "Elantra", "وكيل", "قديم" });
        var result = new ExcelImportService().Read(stream, Array.Empty<Warehouse>(), Branches);
        Assert.Equal(2, result.Vins.Count);
        Assert.Equal("وكيل بغداد", result.Vins[0].Dealer);
        Assert.Equal(2025, result.Vins[0].ManufactureYear);
        Assert.Null(result.Vins[1].Dealer);
        Assert.Null(result.Vins[1].ManufactureYear);
        Assert.Contains(result.Errors, x => x.Contains("سطر 4") && x.Contains("سنة الصنع"));
    }

    [Fact]
    public void Arabic_headers_extra_columns_and_numeric_codes_work()
    {
        using var stream = Workbook(new[] { "رقم القطعة", "الوصف", "الموقع", "الكمية النظامية", "رقم الفرع", "إضافي" },
            new object[] { "PART-02", "فلتر", "A-2", "٢", 904d, "ignored" });
        var result = new ExcelImportService().Read(stream, Array.Empty<Warehouse>(), Branches);
        Assert.Empty(result.Errors); Assert.Equal(2, Assert.Single(result.Parts).SystemQuantity);
    }

    [Fact]
    public void Unknown_missing_and_mismatching_branches_have_clear_row_errors()
    {
        using var stream = Workbook(new[] { "Part Number", "System Quantity", "Branch", "BranchNo." },
            new object[] { "A", 1, "النجف", "" }, new object[] { "B", 1, "", "" }, new object[] { "C", 1, "أربيل", 904 });
        var result = new ExcelImportService().Read(stream, Array.Empty<Warehouse>(), Branches);
        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(result.Errors, x => x.Contains("سطر 2") && x.Contains("النجف") && x.Contains("غير موجود"));
        Assert.Contains(result.Errors, x => x.Contains("سطر 3") && x.Contains("الفرع مطلوب"));
        Assert.Contains(result.Errors, x => x.Contains("سطر 4") && x.Contains("لا يطابق"));
        Assert.Empty(result.Parts);
    }

    [Fact]
    public void Header_only_export_is_not_importable_but_can_accompany_a_populated_report()
    {
        using var empty = Workbook(new[] { "Part Number", "Description", "Location In Stock", "System Quantity", "Branch", "Links" });
        var importer = new ExcelImportService();
        var emptyBytes = empty.ToArray();
        var result = importer.Read(empty, Array.Empty<Warehouse>(), Branches);
        Assert.Contains("عناوين الأعمدة فقط", Assert.Single(result.Errors));
        using var emptyAgain = new MemoryStream(emptyBytes);
        using var data = Workbook(new[] { "Part Number", "System Quantity", "Branch" }, new object[] { "A", 0, "بصرة" });
        result = importer.Read(new[] { ("empty.xlsx", (Stream)emptyAgain), ("data.xlsx", (Stream)data) }, Array.Empty<Warehouse>(), Branches);
        Assert.Empty(result.Errors); Assert.Single(result.Parts);
    }

    [Fact]
    public async Task Failed_import_is_atomic_and_closed_session_still_reserves_its_year()
    {
        using var factory = new ApiFactory { Demo = true };
        await using var services = factory.Services.CreateAsyncScope();
        var db = services.ServiceProvider.GetRequiredService<AppDbContext>();
        var svc = services.ServiceProvider.GetRequiredService<SessionService>();
        var main = await db.Admins.SingleAsync(x => x.Role == AdminRole.MainAdmin);
        var scope = new AdminScope(main.Id, main.Name, main.Role, null);
        var open = await db.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress);
        var year = open.Year;
        Assert.True((await svc.EndAsync(open.Id, scope)).Ok);
        var sessions = await db.Sessions.CountAsync(); var parts = await db.InventoryParts.CountAsync();
        using var file = Workbook(new[] { "Part Number", "System Quantity", "Branch" }, new object[] { "OK", 3, "البصرة" }, new object[] { "BAD", 3, "النجف" });
        var duplicateYear = await svc.StartAsync(year, file, "export.xlsx", file.Length, scope);
        Assert.False(duplicateYear.Ok); Assert.Contains("مكتملة", duplicateYear.Error); Assert.Contains(year.ToString(), duplicateYear.Error);
        file.Position = 0;
        var failed = await svc.StartAsync(year + 1, file, "export.xlsx", file.Length, scope);
        Assert.False(failed.Ok); Assert.Contains("النجف", failed.Error);
        Assert.Equal(sessions, await db.Sessions.CountAsync()); Assert.Equal(parts, await db.InventoryParts.CountAsync());
        Assert.False(await db.Sessions.AnyAsync(x => x.Status == SessionStatus.InProgress));
    }

    [Fact]
    public async Task Dashboard_shows_sticky_Arabic_import_error_and_explicit_year_input()
    {
        using var factory = new ApiFactory { Demo = true };
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        static string Token(string html) => WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var html = await client.GetStringAsync("/Account/Login");
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["PhoneNumber"] = DemoSeeder.MainPhone, ["Password"] = "Test1234", ["__RequestVerificationToken"] = Token(html) }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        html = await client.GetStringAsync("/Sessions");
        Assert.Contains("name=\"year\"", html);
        await using var services = factory.Services.CreateAsyncScope(); var db = services.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var session in await db.Sessions.Where(x => x.Status == SessionStatus.InProgress).ToListAsync()) session.Status = SessionStatus.Completed;
        await db.SaveChangesAsync();
        using var data = Workbook(new[] { "Part Number", "System Quantity", "Branch" }, new object[] { "A", 1, "البصرة" });
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(Token(html)), "__RequestVerificationToken");
        form.Add(new StringContent(DateTime.UtcNow.Year.ToString()), "year");
        form.Add(new ByteArrayContent(data.ToArray()), "file", "export.xlsx");
        var response = await client.PostAsync("/Sessions/Start", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("data-sticky", page); Assert.Contains("role=\"alert\"", page);
        Assert.Contains("لا يمكن إنشاء أكثر من جلسة للسنة نفسها", WebUtility.HtmlDecode(page));
    }
}
