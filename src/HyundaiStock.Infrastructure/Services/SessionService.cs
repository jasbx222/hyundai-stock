using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HyundaiStock.Infrastructure.Services;

public sealed class StorageOptions
{
    /// <summary>Folder for uploaded session files (relative to content root or absolute).</summary>
    public string UploadsPath { get; set; } = "App_Data/uploads";
    public long MaxUploadBytes { get; set; } = 20 * 1024 * 1024;
}

public sealed class ImportResult
{
    public List<InventoryPart> Parts { get; } = new();
    public List<ExpectedVin> Vins { get; } = new();
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Reads the session workbook (.xlsx or .xls) in the Hyundai system format:
/// sheet «Parts»: Quantity | Description | Location | Serial | BranchNo.  (Serial = part number, BranchNo. = Branch.Number)
/// sheet «Vins»:  VIN | WarehouseNo.  (WarehouseNo. = Warehouse.Number, may be empty)
/// Sheets are found by name, or by their headers. Part numbers are kept as text; locations are kept as written («NULL», «-»).
/// The same part number may repeat in a branch (one row per stock location).
/// </summary>
public sealed class ExcelImportService
{
    static ExcelImportService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static readonly string[] PartsHeaders = { "Quantity", "Description", "Location", "Serial", "BranchNo." };
    public static readonly string[] VinsHeaders = { "VIN", "WarehouseNo." };

    private static readonly string[] Serial = { "serial", "partnumber", "partno" };
    private static readonly string[] Desc = { "description" };
    private static readonly string[] Qty = { "quantity", "qty", "systemquantity" };
    private static readonly string[] Loc = { "location", "locationinstock" };
    private static readonly string[] BranchNo = { "branchno", "branchnumber", "branch" };
    private static readonly string[] VinH = { "vin" };
    private static readonly string[] WhNo = { "warehouseno", "warehousenumber", "warehouse" };

    private static string Key(object? o) => new string((o?.ToString() ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Cell → text without losing anything: whole numbers have no ".0", strings are trimmed as-is.</summary>
    public static string Text(object? o) => o switch
    {
        null or DBNull => "",
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => d.ToString("0", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        _ => o.ToString()!.Trim()
    };

    private static bool TryInt(object? o, out int v)
    {
        v = 0;
        switch (o)
        {
            case double d when d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue: v = (int)d; return true;
            case int i: v = i; return true;
            default:
                var t = Text(o);
                if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return true;
                return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var dd) && dd == Math.Floor(dd) && dd is >= int.MinValue and <= int.MaxValue && (v = (int)dd) == dd;
        }
    }

    private static int Find(object?[] header, string[] names)
    {
        for (int c = 0; c < header.Length; c++) if (names.Contains(Key(header[c]))) return c;
        return -1;
    }

    private static string Cut(string v, int max) => v.Length > max ? v[..max] : v;

    public ImportResult Read(Stream stream, IReadOnlyList<Warehouse> warehouses, IReadOnlyList<Branch> branches)
    {
        var res = new ImportResult();
        var sheets = new List<(string name, List<object?[]> rows)>();
        try
        {
            using var reader = ExcelReaderFactory.CreateReader(stream);
            do
            {
                var rows = new List<object?[]>();
                while (reader.Read())
                {
                    var r = new object?[reader.FieldCount];
                    for (int c = 0; c < r.Length; c++) r[c] = reader.GetValue(c);
                    rows.Add(r);
                }
                sheets.Add((reader.Name ?? "", rows));
            } while (reader.NextResult());
        }
        catch (Exception)
        {
            res.Errors.Add("الملف ليس ملف Excel صالحًا (.xlsx أو .xls)");
            return res;
        }
        var brByNo = branches.Where(x => x.Number != null).ToDictionary(x => x.Number!.Value);
        var whByNo = warehouses.Where(x => x.Number != null).ToDictionary(x => x.Number!.Value);
        bool any = false;
        foreach (var (name, rows) in sheets)
        {
            if (rows.Count == 0) continue;
            var h = rows[0];
            int cVin = Find(h, VinH), cSerial = Find(h, Serial);
            bool isVins = Key(name) == "vins" || (cVin >= 0 && cSerial < 0);
            bool isParts = Key(name) == "parts" || (!isVins && cSerial >= 0);
            if (isVins)
            {
                any = true;
                int cWh = Find(h, WhNo);
                if (cVin < 0) { res.Errors.Add($"ورقة {name}: عمود «VIN» مفقود"); continue; }
                var seen = new HashSet<string>();
                for (int r = 1; r < rows.Count; r++)
                {
                    var row = rows[r];
                    // VINs in the system file sometimes carry a trailing marker such as «*»: keep letters/digits only.
                    var vin = new string(Text(row[cVin]).ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
                    if (vin.Length == 0) continue; // blank rows (the system file has many)
                    if (!Validators.IsVin(vin)) { res.Errors.Add($"{name} سطر {r + 1}: VIN غير صالح «{vin}»"); continue; }
                    if (!seen.Add(vin)) continue; // repeated VIN in the file: import once
                    Warehouse? w = null;
                    var wt = cWh >= 0 ? Text(row[cWh]) : "";
                    if (wt.Length > 0)
                    {
                        if (!TryInt(row[cWh], out var wn)) { res.Errors.Add($"{name} سطر {r + 1}: رقم المخزن غير صالح «{wt}»"); continue; }
                        if (!whByNo.TryGetValue(wn, out w)) { res.Errors.Add($"{name} سطر {r + 1}: المخزن رقم {wn} غير موجود في النظام"); continue; }
                    }
                    res.Vins.Add(new ExpectedVin { Vin = vin, WarehouseId = w?.Id, BranchId = w?.BranchId });
                }
            }
            else if (isParts)
            {
                any = true;
                int cDesc = Find(h, Desc), cQty = Find(h, Qty), cLoc = Find(h, Loc), cBr = Find(h, BranchNo);
                if (cSerial < 0 || cQty < 0 || cBr < 0) { res.Errors.Add($"ورقة {name}: يجب توفر الأعمدة «Serial» و«Quantity» و«BranchNo.»"); continue; }
                for (int r = 1; r < rows.Count; r++)
                {
                    var row = rows[r]; var pn = Text(row[cSerial]);
                    if (pn.Length == 0 && row.All(x => Text(x).Length == 0)) continue;
                    if (pn.Length == 0 || pn.Length > 64) { res.Errors.Add($"{name} سطر {r + 1}: رقم القطعة (Serial) غير صالح"); continue; }
                    int q = 0;
                    if (Text(row[cQty]).Length > 0 && !TryInt(row[cQty], out q)) { res.Errors.Add($"{name} سطر {r + 1}: الكمية غير صالحة"); continue; }
                    if (q < 0) { res.Errors.Add($"{name} سطر {r + 1}: الكمية سالبة"); continue; }
                    if (!TryInt(row[cBr], out var bn)) { res.Errors.Add($"{name} سطر {r + 1}: رقم الفرع (BranchNo.) غير صالح"); continue; }
                    if (!brByNo.TryGetValue(bn, out var br)) { res.Errors.Add($"{name} سطر {r + 1}: الفرع رقم {bn} غير موجود في النظام"); continue; }
                    var loc = cLoc >= 0 ? Text(row[cLoc]) : "";
                    res.Parts.Add(new InventoryPart { PartNumber = pn, Description = cDesc >= 0 ? Cut(Text(row[cDesc]), 400) : "", SystemQuantity = q,
                        SystemLocation = loc.Length > 0 ? Cut(loc, 64) : null, BranchId = br.Id });
                }
            }
        }
        if (!any) res.Errors.Add("يجب أن يحتوي الملف على ورقة «Parts» (Quantity, Description, Location, Serial, BranchNo.) أو ورقة «Vins» (VIN, WarehouseNo.)");
        else if (res.Errors.Count == 0 && res.Parts.Count == 0 && res.Vins.Count == 0) res.Errors.Add("الملف فارغ");
        return res;
    }
}

public sealed class SessionService(AppDbContext db, ExcelImportService importer, IOptions<StorageOptions> opt, Microsoft.Extensions.Hosting.IHostEnvironment env)
{
    private string Dir
    {
        get
        {
            var p = opt.Value.UploadsPath;
            var full = Path.IsPathRooted(p) ? p : Path.Combine(env.ContentRootPath, p);
            Directory.CreateDirectory(full);
            return full;
        }
    }

    public Task<PagedResult<SessionRow>> ListAsync(PageQuery p)
    {
        var q = db.Sessions.AsNoTracking().AsQueryable();
        if (p.Term is string t) q = q.Where(x => x.Year.ToString().Contains(t) || x.FileName.Contains(t) || x.StartedByAdmin!.Name.Contains(t));
        var rows = q.OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).Select(x => new SessionRow(x.Id, x.Year, x.Status, x.StartedByAdmin!.Name, x.StartedAt, x.EndedAt, x.FileName, x.Parts.Count(), x.ExpectedVins.Count()));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public async Task<OpResult> StartAsync(int year, Stream file, string fileName, long length, AdminScope s)
    {
        if (!s.IsSuper) return OpResult.Fail("بدء الجلسة للسوبر أدمن فقط");
        if (year < 2000 || year > 2100) return OpResult.Fail("السنة غير صالحة");
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is not (".xlsx" or ".xls")) return OpResult.Fail("يجب أن يكون الملف بصيغة .xlsx أو .xls");
        if (length <= 0) return OpResult.Fail("الملف فارغ");
        if (length > opt.Value.MaxUploadBytes) return OpResult.Fail("حجم الملف أكبر من المسموح");
        if (await db.Sessions.AnyAsync(x => x.Status == SessionStatus.InProgress)) return OpResult.Fail("توجد جلسة جرد قيد التنفيذ، أنهِها أولاً");
        if (await db.Sessions.AnyAsync(x => x.Year == year)) return OpResult.Fail($"توجد جلسة جرد لسنة {year}");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var whs = await db.Warehouses.AsNoTracking().ToListAsync();
        var brs = await db.Branches.AsNoTracking().ToListAsync();
        var res = importer.Read(ms, whs, brs);
        if (res.Errors.Count > 0) return OpResult.Fail(string.Join("\n", res.Errors.Take(15)) + (res.Errors.Count > 15 ? $"\n… و{res.Errors.Count - 15} أخطاء أخرى" : ""));

        var stored = $"{Guid.NewGuid():N}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(Dir, stored), ms.ToArray());
        var session = new StocktakingSession { Year = year, StartedByAdminId = s.Id, StartedAt = DateTime.UtcNow, FileName = Path.GetFileName(fileName), StoredFileName = stored };
        // Fast path for ~16k rows: save the session, then bulk-add rows with change tracking off, in one transaction.
        // Fast path for ~16k rows: save the session, then bulk-add rows with change tracking off, all in one transaction
        // (wrapped in the execution strategy so SQL Server retry-on-failure works with a user transaction).
        var prevDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync();
                db.ChangeTracker.AutoDetectChangesEnabled = true;
                session.Id = 0;
                db.Sessions.Add(session);
                await db.SaveChangesAsync();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var now = DateTime.UtcNow;
                foreach (var chunk in res.Parts.Chunk(5000))
                {
                    foreach (var p in chunk) { p.Id = 0; p.SessionId = session.Id; p.CreatedAt = now; }
                    db.InventoryParts.AddRange(chunk);
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                }
                foreach (var chunk in res.Vins.Chunk(5000))
                {
                    foreach (var v in chunk) { v.Id = 0; v.SessionId = session.Id; v.CreatedAt = now; }
                    db.ExpectedVins.AddRange(chunk);
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                }
                await tx.CommitAsync();
            });
        }
        finally { db.ChangeTracker.AutoDetectChangesEnabled = prevDetect; }
        return OpResult.Success(session.Id);
    }

    public async Task<OpResult> EndAsync(int id, AdminScope s)
    {
        if (!s.IsSuper) return OpResult.Fail("إنهاء الجلسة للسوبر أدمن فقط");
        var e = await db.Sessions.FindAsync(id);
        if (e == null) return OpResult.Fail("الجلسة غير موجودة");
        if (e.Status != SessionStatus.InProgress) return OpResult.Fail("الجلسة منتهية");
        e.Status = SessionStatus.Completed; e.EndedAt = DateTime.UtcNow; e.EndedByAdminId = s.Id;
        await db.SaveChangesAsync();
        return OpResult.Success();
    }

    public async Task<(string path, string name)?> FileAsync(int id)
    {
        var e = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e == null || string.IsNullOrEmpty(e.StoredFileName)) return null;
        var path = Path.Combine(Dir, Path.GetFileName(e.StoredFileName));
        return File.Exists(path) ? (path, e.FileName) : null;
    }
}
