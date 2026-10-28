using ClosedXML.Excel;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public enum PartExportKind { All, Counted, Uncounted }

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

    private static ExportFile Build(string sheet, string fileBase, string[] headers, IEnumerable<object?[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheet.Length > 31 ? sheet[..31] : sheet);
        ws.RightToLeft = true;
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        var hr = ws.Range(1, 1, 1, headers.Length);
        hr.Style.Font.Bold = true; hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#002C5F"); hr.Style.Font.FontColor = XLColor.White;
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
                    case DateTime dt: cell.Value = Local(dt); cell.Style.DateFormat.Format = "yyyy/MM/dd HH:mm"; break;
                    default: cell.Value = row[c]!.ToString(); break;
                }
            }
            r++;
        }
        ws.SheetView.FreezeRows(1);
        if (r > 2) ws.Range(1, 1, r - 1, headers.Length).SetAutoFilter();
        ws.Columns().AdjustToContents(1, Math.Min(r, 500));
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var safe = string.Concat(fileBase.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '-' : ch));
        return new ExportFile(ms.ToArray(), $"{safe}-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
    }

    public async Task<ExportFile> PartsAsync(PartExportKind kind, ExportFilter f, AdminScope scope)
    {
        var (sid, label) = await ResolveSession(f);
        var b = await BranchFilter(f, scope);
        var q = db.InventoryParts.AsNoTracking().Where(x => x.SessionId == sid);
        if (b is int bb) q = q.Where(x => x.BranchId == bb);
        if (f.WarehouseId is int w) q = q.Where(x => x.WarehouseId == w);
        if (f.UserId is int u) q = q.Where(x => x.CountedByUserId == u);
        if (kind == PartExportKind.Counted) q = q.Where(x => x.CountedQuantity != null);
        if (kind == PartExportKind.Uncounted) q = q.Where(x => x.CountedQuantity == null);
        var data = await q.OrderBy(x => x.PartNumber).Select(x => new
        {
            x.PartNumber, x.Description, x.SystemQuantity, x.CountedQuantity, x.SystemLocation, x.CountedLocation,
            Wh = x.Warehouse != null ? x.Warehouse.Name : "", Br = x.Branch != null ? x.Branch.Name : "", User = x.CountedByUser != null ? x.CountedByUser.Name : "", x.CountedAt
        }).ToListAsync();
        var name = kind switch { PartExportKind.Counted => "قطع-الجرد", PartExportKind.Uncounted => "قطع-غير-معدودة", _ => "قطع-الغيار" };
        return Build(name, $"{name}-{label}",
            new[] { "رقم القطعة", "الوصف", "الكمية في النظام", "الكمية المعدودة", "الفرق", "الموقع في المخزون", "الموقع المحسوب", "المخزن", "الفرع", "المستخدم", "تاريخ العد" },
            data.Select(x => new object?[] { x.PartNumber, x.Description, x.SystemQuantity, x.CountedQuantity, x.CountedQuantity.HasValue ? x.CountedQuantity - x.SystemQuantity : null,
                x.SystemLocation, x.CountedLocation, x.Wh, x.Br, x.User, x.CountedAt }));
    }

    public async Task<ExportFile> ScannedVinsAsync(ExportFilter f, AdminScope scope, bool allSessions = false)
    {
        var (sid, label) = await ResolveSession(f);
        var b = await BranchFilter(f, scope);
        var q = db.ScannedVins.AsNoTracking().AsQueryable();
        if (!allSessions || f.SessionId != null) q = q.Where(x => x.SessionId == sid);
        if (b is int bb) q = q.Where(x => x.BranchId == bb);
        if (f.WarehouseId is int w) q = q.Where(x => x.WarehouseId == w);
        if (f.UserId is int u) q = q.Where(x => x.UserId == u);
        var data = await q.OrderByDescending(x => x.CreatedAt).Select(x => new { x.Vin, x.Session!.Year, U = x.User!.Name, W = x.Warehouse!.Name, B = x.Branch!.Name, x.Latitude, x.Longitude, x.CreatedAt }).ToListAsync();
        return Build("السيارات", $"السيارات-{(allSessions && f.SessionId == null ? "كل-الجلسات" : label)}",
            new[] { "VIN", "اسم الجلسة", "المستخدم", "المستودع", "الفرع", "خط العرض", "خط الطول", "تاريخ الإضافة" },
            data.Select(x => new object?[] { x.Vin, $"جلسة جرد {x.Year}", x.U, x.W, x.B, x.Latitude, x.Longitude, x.CreatedAt }));
    }

    public async Task<ExportFile> ExpectedVinsAsync(ExportFilter f, AdminScope scope, bool missingOnly)
    {
        var (sid, label) = await ResolveSession(f);
        var b = await BranchFilter(f, scope);
        var q = db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == sid);
        if (b is int bb) q = q.Where(x => x.BranchId == bb);
        if (f.WarehouseId is int w) q = q.Where(x => x.WarehouseId == w);
        var data = await q.OrderBy(x => x.Vin).Select(x => new
        {
            x.Vin, x.Model, W = x.Warehouse != null ? x.Warehouse.Name : "", B = x.Branch != null ? x.Branch.Name : "",
            Scanned = db.ScannedVins.Any(s => s.SessionId == x.SessionId && s.Vin == x.Vin)
        }).ToListAsync();
        if (missingOnly) data = data.Where(x => !x.Scanned).ToList();
        var name = missingOnly ? "VINs-غير-موجودة" : "VINs";
        return Build(name, $"{name}-{label}", new[] { "VIN", "الموديل", "المستودع", "الفرع", "تم المسح" },
            data.Select(x => new object?[] { x.Vin, x.Model, x.W, x.B, x.Scanned ? "نعم" : "لا" }));
    }

    public async Task<ExportFile> BranchesAsync(AdminScope scope)
    {
        var q = db.Branches.AsNoTracking().AsQueryable();
        if (scope.ScopeBranch is int b) q = q.Where(x => x.Id == b);
        var data = await q.OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, W = x.Warehouses.Count() }).ToListAsync();
        return Build("الفروع", "الفروع", new[] { "الرقم", "اسم الفرع", "عدد المخازن" }, data.Select(x => new object?[] { x.Id, x.Name, x.W }));
    }

    public async Task<ExportFile> WarehousesAsync(int? branchId, AdminScope scope)
    {
        var q = db.Warehouses.AsNoTracking().AsQueryable();
        if (scope.ScopeBranch is int b) q = q.Where(x => x.BranchId == b);
        if (branchId is int f) q = q.Where(x => x.BranchId == f);
        var data = await q.OrderBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Type, B = x.Branch!.Name }).ToListAsync();
        return Build("المخازن", "المخازن", new[] { "الرقم", "اسم المخزن", "النوع", "الفرع" }, data.Select(x => new object?[] { x.Id, x.Name, Labels.Type(x.Type), x.B }));
    }
}
