using ClosedXML.Excel;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public enum PartExportKind { All, Counted, Uncounted }

public sealed class FinanceFilter
{
    public int? SessionId { get; set; }
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public string? PartNumber { get; set; }
    public string? Description { get; set; }
    public string? Difference { get; set; }
    public string? Sort { get; set; }
}

public sealed class ExportFilter
{
    public int? SessionId { get; set; }
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public int? UserId { get; set; }
    public int? AdminId { get; set; }
}

public sealed record ExportFile(byte[] Content, string FileName)
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

public sealed class ExportService(AppDbContext db, SessionLookup sessions)
{
    private static readonly TimeZoneInfo Tz = ResolveTz();
    private static TimeZoneInfo ResolveTz()
    {
        foreach (var id in new[] { "Asia/Baghdad", "Arabic Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.CreateCustomTimeZone("Baghdad", TimeSpan.FromHours(3), "Baghdad", "Baghdad");
    }
    public static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);

    private async Task<(int sessionId, string label)> ResolveSession(ExportFilter f)
    {
        StocktakingSession? s = f.SessionId is int id ? await db.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) : await sessions.LatestAsync();
        return s == null ? (-1, "بدون-جلسة") : (s.Id, s.Year.ToString());
    }

    private async Task<int?> BranchFilter(ExportFilter f, AdminScope scope)
    {
        int? b = f.BranchId;
        if (f.AdminId is int aid) b = await db.Admins.Where(x => x.Id == aid).Select(x => x.BranchId).FirstOrDefaultAsync() ?? -1;
        if (scope.ScopeBranch is int sb) b = b == null || b == sb ? sb : -1;
        return b;
    }

    /// <summary>Text form used by the Hyundai system files: local time, "yyyy-MM-dd HH:mm:ss".</summary>
    public static string Stamp(DateTime? utc) => utc is DateTime d ? Local(d).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) : "";
    public static string MapLink(double? lat, double? lng) =>
        lat is double a && lng is double b ? FormattableString.Invariant($"https://www.google.com/maps?q={a},{b}") : "";

    /// <summary>
    /// Builds a sheet in the exact layout of the Hyundai system exports: LTR, header row bold black on light grey (#D3D3D3)
    /// with thin borders, fixed column widths, General number format, no freeze pane, no autofilter.
    /// Strings are always written as text so part numbers keep their leading zeros.
    /// </summary>
    private static ExportFile Build(string sheet, string fileBase, string[] headers, double[] widths, IEnumerable<object?[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheet.Length > 31 ? sheet[..31] : sheet);
        for (int i = 0; i < headers.Length; i++)
        {
            var h = ws.Cell(1, i + 1);
            h.Value = headers[i];
            h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.Black;
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#D3D3D3");
            h.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        // ClosedXML stores width + ~0.71 padding; subtract it so the saved widths equal the Hyundai files.
        for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i] - 0.71;
        int r = 2;
        foreach (var row in rows)
        {
            for (int c = 0; c < row.Length; c++)
            {
                var cell = ws.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    case int n: cell.Value = n; break;
                    case decimal d: cell.Value = d; break;
                    case double d2: cell.Value = d2; break;
                    case DateTime dt: cell.Value = Stamp(dt); break;
                    default: cell.SetValue(row[c]!.ToString()); break;
                }
            }
            r++;
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var safe = string.Concat(fileBase.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        return new ExportFile(ms.ToArray(), $"{safe}-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    public static readonly string[] DataHeaders = { "Part Number", "Description", "Location In Stock", "System Quantity", "Total Counted", "Difference", "Counted Location", "Counted Quantity", "User", "Branch", "Date", "Links" };
    private static readonly double[] DataWidths = { 14.21, 12.34, 18.16, 16.99, 15.59, 11.19, 18.11, 18.11, 9.14, 9.14, 9.14, 9.14 };
    public static readonly string[] NotCountedHeaders = { "Part Number", "Description", "Location In Stock", "System Quantity", "Branch" };
    private static readonly double[] NotCountedWidths = { 27.61, 54.76, 23.18, 16.99, 9.14 };
    public static readonly string[] VinsHeaders = { "VIN", "Warehouse", "Branch", "User", "Date", "Latitude", "Longitude", "Map Link" };
    private static readonly double[] VinsWidths = { 9.14, 12.38, 9.14, 9.14, 9.14, 9.14, 10.98, 9.84 };
    public static readonly string[] CarsNotFoundHeaders = { "VIN", "Warehouse", "Branch", "Date Added" };
    private static readonly double[] CarsNotFoundWidths = { 23.72, 28.08, 9.14, 22.43 };
    public static readonly string[] WarehousesHeaders = { "WarehouseNo.", "Name", "Branch" };
    private static readonly double[] WarehousesWidths = { 16.48, 28.08, 9.14 };

    /// <summary>
    /// Counted / all parts → sheet "Data"; not counted → sheet "Not Counted Parts" (Hyundai system layout).
    /// Each inventory row stays its own line. Total Counted and Difference are that row only, so a sibling with the same part number does not mark this row counted.
    /// </summary>
    public async Task<ExportFile> PartsAsync(PartExportKind kind, ExportFilter f, AdminScope scope)
    {
        var (sid, label) = await ResolveSession(f);
        var b = await BranchFilter(f, scope);
        var q = db.InventoryParts.AsNoTracking().Where(x => x.SessionId == sid);
        if (b is int bb) q = q.Where(x => x.BranchId == bb);
        if (f.WarehouseId is int w)
        {
            var wb = await db.Warehouses.Where(z => z.Id == w).Select(z => (int?)z.BranchId).FirstOrDefaultAsync();
            q = q.Where(x => x.WarehouseId == w || (x.WarehouseId == null && x.BranchId == wb));
        }
        if (f.UserId is int u) q = q.Where(x => x.CountedByUserId == u);
        if (kind == PartExportKind.Counted) q = q.Where(x => x.CountedQuantity != null);
        if (kind == PartExportKind.Uncounted) q = q.Where(x => x.CountedQuantity == null);
        var data = await q.OrderBy(x => x.BranchId).ThenBy(x => x.PartNumber).ThenBy(x => x.Id).Select(x => new
        {
            x.PartNumber, x.Description, x.SystemQuantity, x.CountedQuantity, x.SystemLocation, x.CountedLocation, x.BranchId,
            Br = x.Branch != null ? x.Branch.Name : "", User = x.CountedByUser != null ? x.CountedByUser.Name : "", x.CountedAt, x.Latitude, x.Longitude
        }).ToListAsync();
        if (kind == PartExportKind.Uncounted)
            return Build("Not Counted Parts", $"Not-Counted-Parts-{label}", NotCountedHeaders, NotCountedWidths,
                data.Select(x => new object?[] { x.PartNumber, x.Description, x.SystemLocation ?? "", x.SystemQuantity, x.Br }));

        return Build("Data", $"{(kind == PartExportKind.Counted ? "Counted-Parts" : "Parts")}-{label}", DataHeaders, DataWidths,
            data.Select(x => new object?[] { x.PartNumber, x.Description, x.SystemLocation ?? "", x.SystemQuantity,
                x.CountedQuantity, x.CountedQuantity is int c ? c - x.SystemQuantity : null, x.CountedLocation, x.CountedQuantity, x.User, x.Br,
                x.CountedAt is DateTime d ? Stamp(d) : "", MapLink(x.Latitude, x.Longitude) }));
    }

    /// <summary>Scanned VINs → sheet "Vins".</summary>
    public async Task<ExportFile> ScannedVinsAsync(ExportFilter f, AdminScope scope, bool allSessions = false)
    {
        var (sid, label) = await ResolveSession(f);
        var b = await BranchFilter(f, scope);
        var q = db.ScannedVins.AsNoTracking().AsQueryable();
        if (!allSessions || f.SessionId != null) q = q.Where(x => x.SessionId == sid);
        if (b is int bb) q = q.Where(x => x.BranchId == bb);
        if (f.WarehouseId is int w) q = q.Where(x => x.WarehouseId == w);
        if (f.UserId is int u) q = q.Where(x => x.UserId == u);
        var data = await q.OrderByDescending(x => x.CreatedAt).Select(x => new { x.Vin, U = x.User!.Name, W = x.Warehouse!.Name, B = x.Branch!.Name, x.Latitude, x.Longitude, x.CreatedAt }).ToListAsync();
        return Build("Vins", $"Vins-{(allSessions && f.SessionId == null ? "all" : label)}", VinsHeaders, VinsWidths,
            data.Select(x => new object?[] { x.Vin, x.W, x.B, x.U, Stamp(x.CreatedAt), x.Latitude, x.Longitude, MapLink(x.Latitude, x.Longitude) }));
    }

    /// <summary>Expected VINs; missing only → sheet "Cars Not Found" (Date Added = when the VIN was imported).</summary>
    public async Task<ExportFile> ExpectedVinsAsync(ExportFilter f, AdminScope scope, bool missingOnly)
    {
        var (sid, label) = await ResolveSession(f);
        var b = await BranchFilter(f, scope);
        var q = db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == sid);
        if (b is int bb) q = q.Where(x => x.BranchId == bb);
        if (f.WarehouseId is int w) q = q.Where(x => x.WarehouseId == w);
        if (missingOnly) q = q.Where(x => !db.ScannedVins.Any(s => s.SessionId == x.SessionId && s.Vin == x.Vin));
        var data = await q.OrderBy(x => x.Vin).Select(x => new
        {
            x.Vin, W = x.Warehouse != null ? x.Warehouse.Name : "", B = x.Branch != null ? x.Branch.Name : "", x.CreatedAt
        }).ToListAsync();
        return Build(missingOnly ? "Cars Not Found" : "Cars", $"{(missingOnly ? "Cars-Not-Found" : "Cars")}-{label}", CarsNotFoundHeaders, CarsNotFoundWidths,
            data.Select(x => new object?[] { x.Vin, x.W, x.B, Stamp(x.CreatedAt) }));
    }

    public async Task<ExportFile> BranchesAsync(AdminScope scope)
    {
        var q = db.Branches.AsNoTracking().AsQueryable();
        if (scope.ScopeBranch is int b) q = q.Where(x => x.Id == b);
        var data = await q.OrderBy(x => x.Number ?? int.MaxValue).ThenBy(x => x.Id).Select(x => new { x.Number, x.Name }).ToListAsync();
        return Build("Branches", "Branches", new[] { "BranchNo.", "Name" }, new[] { 16.48, 28.08 }, data.Select(x => new object?[] { x.Number, x.Name }));
    }

    /// <summary>Warehouses → sheet "Warehouses" (WarehouseNo., Name, Branch), same as the Hyundai reference file.</summary>
    public async Task<ExportFile> WarehousesAsync(int? branchId, AdminScope scope)
    {
        var q = db.Warehouses.AsNoTracking().AsQueryable();
        if (scope.ScopeBranch is int b) q = q.Where(x => x.BranchId == b);
        if (branchId is int f) q = q.Where(x => x.BranchId == f);
        var data = await q.OrderBy(x => x.Number ?? int.MaxValue).ThenBy(x => x.Id).Select(x => new { x.Number, x.Name, B = x.Branch!.Name }).ToListAsync();
        return Build("Warehouses", "Warehouses", WarehousesHeaders, WarehousesWidths, data.Select(x => new object?[] { x.Number, x.Name, x.B }));
    }

    public sealed record FinanceRow(int Id, string PartNumber, string Description, int SystemQuantity, int? CountedQuantity, int? Difference, decimal Price, decimal? FinancialDifference, string? Branch, string? Warehouse, int Year);

    public async Task<(PagedResult<FinanceRow>? Page, string? Error, bool Forbidden)> FinanceListAsync(FinanceFilter f, PageQuery p, AdminScope s)
    {
        var built = await FinanceSource(f, s);
        if (built.Query == null) return (null, built.Error, built.Forbidden);
        var rows = FinanceOrder(built.Query, f.Sort).Select(x => new FinanceRow(x.Id, x.PartNumber, x.Description, x.SystemQuantity, x.CountedQuantity,
            x.CountedQuantity == null ? null : x.CountedQuantity - x.SystemQuantity, x.Price,
            x.CountedQuantity == null ? null : decimal.Round((x.CountedQuantity.Value - x.SystemQuantity) * x.Price, 2, MidpointRounding.AwayFromZero),
            x.Branch != null ? x.Branch.Name : null, x.Warehouse != null ? x.Warehouse.Name : null, x.Session!.Year));
        return (Paging.Build(rows, p), null, false);
    }

    public async Task<(ExportFile? File, string? Error, bool Forbidden)> FinanceFileAsync(FinanceFilter f, AdminScope s)
    {
        var built = await FinanceSource(f, s);
        if (built.Query == null) return (null, built.Error, built.Forbidden);
        var data = await FinanceOrder(built.Query, f.Sort).Select(x => new { x.PartNumber, x.Description, x.SystemQuantity, x.CountedQuantity, x.Price, B = x.Branch != null ? x.Branch.Name : "", W = x.Warehouse != null ? x.Warehouse.Name : "", Y = x.Session!.Year }).ToListAsync();
        var file = Build("Variance", "Financial-Variance",
            new[] { "Part Number", "Description", "System Quantity", "Counted Quantity", "Difference", "Price", "Financial Difference", "Branch", "Warehouse", "Session" },
            new[] { 22d, 40, 16, 16, 14, 14, 18, 18, 18, 12 },
            data.Select(x =>
            {
                int? d = x.CountedQuantity == null ? null : x.CountedQuantity - x.SystemQuantity;
                decimal? m = d == null ? null : decimal.Round(d.Value * x.Price, 2, MidpointRounding.AwayFromZero);
                return new object?[] { x.PartNumber, x.Description, x.SystemQuantity, x.CountedQuantity, d, x.Price, m, x.B, x.W, x.Y };
            }));
        return (file, null, false);
    }

    private async Task<(IQueryable<InventoryPart>? Query, string? Error, bool Forbidden)> FinanceSource(FinanceFilter f, AdminScope s)
    {
        if (!MenuAllowedFinance(s)) return (null, "ليس لديك صلاحية لتنفيذ هذه العملية", true);
        var sid = f.SessionId ?? (await sessions.LatestAsync())?.Id ?? -1;
        var q = db.InventoryParts.AsNoTracking().Where(x => x.SessionId == sid);
        if (!s.IsUnrestricted)
        {
            var branches = s.Branches.ToList();
            q = q.Where(x => x.BranchId != null && branches.Contains(x.BranchId.Value));
            if (f.BranchId is int b && !branches.Contains(b)) return (null, "ليس لديك صلاحية لتنفيذ هذه العملية", true);
        }
        if (f.BranchId is int bid) q = q.Where(x => x.BranchId == bid);
        if (f.WarehouseId is int wid)
        {
            var br = await db.Warehouses.Where(x => x.Id == wid).Select(x => (int?)x.BranchId).FirstOrDefaultAsync();
            if (br == null || (!s.IsUnrestricted && !s.Branches.Contains(br.Value))) return (null, "ليس لديك صلاحية لتنفيذ هذه العملية", true);
            q = q.Where(x => x.WarehouseId == wid);
        }
        if (!string.IsNullOrWhiteSpace(f.PartNumber)) { var t = f.PartNumber.Trim(); q = q.Where(x => x.PartNumber.Contains(t)); }
        if (!string.IsNullOrWhiteSpace(f.Description)) { var t = f.Description.Trim(); q = q.Where(x => x.Description.Contains(t)); }
        q = f.Difference switch
        {
            "shortage" => q.Where(x => x.CountedQuantity != null && x.CountedQuantity < x.SystemQuantity),
            "surplus" => q.Where(x => x.CountedQuantity != null && x.CountedQuantity > x.SystemQuantity),
            "match" => q.Where(x => x.CountedQuantity != null && x.CountedQuantity == x.SystemQuantity),
            "uncounted" => q.Where(x => x.CountedQuantity == null),
            "diff" => q.Where(x => x.CountedQuantity != null && x.CountedQuantity != x.SystemQuantity),
            _ => q
        };
        return (q, null, false);
    }

    private static bool MenuAllowedFinance(AdminScope s) => s.Role is AdminRole.MainAdmin or AdminRole.SuperAdmin or AdminRole.Accountant or AdminRole.AccountsManager;
    private static IQueryable<InventoryPart> FinanceOrder(IQueryable<InventoryPart> q, string? sort) => sort switch
    {
        "difference" => q.OrderBy(x => x.CountedQuantity == null ? 0 : x.CountedQuantity - x.SystemQuantity).ThenBy(x => x.Id),
        "value" => q.OrderBy(x => x.CountedQuantity == null ? 0m : (x.CountedQuantity - x.SystemQuantity) * x.Price).ThenBy(x => x.Id),
        _ => q.OrderBy(x => x.PartNumber).ThenBy(x => x.Id)
    };

    /// <summary>Empty session import template: sheets "Parts" (Quantity, Description, Location, Serial, BranchNo.) and "Vins" (VIN, WarehouseNo.).</summary>
    public static byte[] ImportTemplate()
    {
        using var wb = new XLWorkbook();
        void Sheet(string name, string[] h, double[] w, object?[] sample)
        {
            var ws = wb.Worksheets.Add(name);
            for (int i = 0; i < h.Length; i++) { ws.Cell(1, i + 1).Value = h[i]; ws.Column(i + 1).Width = w[i]; }
            ws.Range(1, 1, 1, h.Length).Style.Font.Bold = true;
            for (int i = 0; i < sample.Length; i++)
            {
                var c = ws.Cell(2, i + 1);
                if (sample[i] is int n) c.Value = n; else { c.SetValue(sample[i]?.ToString() ?? ""); }
            }
        }
        Sheet("Parts", ExcelImportService.PartsHeaders, new[] { 10.0, 43.33, 19.11, 22.55, 10.0 }, new object?[] { 4, "فلتر زيت", "A-01-02", "0263001080", 2 });
        Sheet("Vins", ExcelImportService.VinsHeaders, new[] { 25.33, 13.33, 30.0 }, new object?[] { "TMAJB81D4TJ653724", 2, "Tucson" });
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
