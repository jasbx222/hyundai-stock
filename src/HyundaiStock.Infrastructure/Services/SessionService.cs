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
/// Reads the session workbook (.xlsx or .xls). Sheet with a «VIN» header = vehicles; any other sheet with a part number header = spare parts.
/// Parts columns: رقم القطعة | الوصف | السعر | الكمية | الموقع | المخزن. VIN columns: VIN | الموديل | المخزن.
/// </summary>
public sealed class ExcelImportService
{
    static ExcelImportService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly string[] PartNo = { "رقم القطعة", "partnumber", "part number", "part no", "partcode", "part code", "رقم الجزء" };
    private static readonly string[] Desc = { "الوصف", "وصف القطعة", "description", "partdescription" };
    private static readonly string[] Price = { "السعر", "price" };
    private static readonly string[] Qty = { "الكمية", "الكمية في النظام", "quantity", "qty", "systemquantity" };
    private static readonly string[] Loc = { "الموقع", "الموقع في المخزون", "location", "bin" };
    private static readonly string[] Wh = { "المخزن", "المستودع", "warehouse" };
    private static readonly string[] VinH = { "vin", "رقم الشاصي", "رقم vin" };
    private static readonly string[] Model = { "الموديل", "model" };

    private static string Norm(object? o) => (o?.ToString() ?? "").Trim();
    private static int Find(DataRow header, string[] names)
    {
        for (int c = 0; c < header.Table.Columns.Count; c++)
        {
            var h = Norm(header[c]).ToLowerInvariant();
            if (names.Any(n => n.ToLowerInvariant() == h)) return c;
        }
        return -1;
    }

    public ImportResult Read(Stream stream, IReadOnlyList<Warehouse> warehouses)
    {
        var res = new ImportResult();
        DataSet ds;
        try
        {
            using var reader = ExcelReaderFactory.CreateReader(stream);
            ds = reader.AsDataSet();
        }
        catch (Exception)
        {
            res.Errors.Add("الملف مو ملف Excel صالح (.xlsx أو .xls)");
            return res;
        }
        var byName = warehouses.GroupBy(w => w.Name.Trim()).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        Warehouse? ResolveWh(string name, string sheet, int row)
        {
            if (string.IsNullOrEmpty(name)) { res.Errors.Add($"{sheet} سطر {row}: المخزن فارغ"); return null; }
            if (byName.TryGetValue(name, out var w)) return w;
            res.Errors.Add($"{sheet} سطر {row}: المخزن «{name}» غير موجود بالنظام");
            return null;
        }
        bool any = false;
        foreach (DataTable t in ds.Tables)
        {
            if (t.Rows.Count == 0) continue;
            var h = t.Rows[0];
            int cVin = Find(h, VinH), cPart = Find(h, PartNo);
            if (cVin >= 0)
            {
                any = true;
                int cModel = Find(h, Model), cWh = Find(h, Wh);
                if (cWh < 0) { res.Errors.Add($"ورقة {t.TableName}: عمود «المخزن» مفقود"); continue; }
                var seen = new HashSet<string>();
                for (int r = 1; r < t.Rows.Count; r++)
                {
                    var row = t.Rows[r]; var vin = Validators.NormalizeVin(Norm(row[cVin]));
                    if (vin.Length == 0 && row.ItemArray.All(x => Norm(x).Length == 0)) continue;
                    if (!Validators.IsVin(vin)) { res.Errors.Add($"{t.TableName} سطر {r + 1}: VIN غير صالح «{vin}»"); continue; }
                    if (!seen.Add(vin)) { res.Errors.Add($"{t.TableName} سطر {r + 1}: VIN مكرر «{vin}»"); continue; }
                    var w = ResolveWh(Norm(row[cWh]), t.TableName, r + 1); if (w == null) continue;
                    res.Vins.Add(new ExpectedVin { Vin = vin, Model = cModel >= 0 ? Norm(row[cModel]) : null, WarehouseId = w.Id, BranchId = w.BranchId });
                }
            }
            else if (cPart >= 0)
            {
                any = true;
                int cDesc = Find(h, Desc), cPrice = Find(h, Price), cQty = Find(h, Qty), cLoc = Find(h, Loc), cWh = Find(h, Wh);
                if (cQty < 0 || cWh < 0) { res.Errors.Add($"ورقة {t.TableName}: لازم أعمدة «الكمية» و«المخزن»"); continue; }
                var seen = new HashSet<string>();
                for (int r = 1; r < t.Rows.Count; r++)
                {
                    var row = t.Rows[r]; var pn = Norm(row[cPart]);
                    if (pn.Length == 0 && row.ItemArray.All(x => Norm(x).Length == 0)) continue;
                    if (pn.Length == 0 || pn.Length > 64) { res.Errors.Add($"{t.TableName} سطر {r + 1}: رقم القطعة غير صالح"); continue; }
                    if (!int.TryParse(Norm(row[cQty]), NumberStyles.Number, CultureInfo.InvariantCulture, out var q) &&
                        !(double.TryParse(Norm(row[cQty]), NumberStyles.Any, CultureInfo.InvariantCulture, out var qd) && (q = (int)qd) == qd))
                    { res.Errors.Add($"{t.TableName} سطر {r + 1}: الكمية غير صالحة"); continue; }
                    if (q < 0) { res.Errors.Add($"{t.TableName} سطر {r + 1}: الكمية سالبة"); continue; }
                    decimal price = 0;
                    if (cPrice >= 0 && Norm(row[cPrice]).Length > 0 && !decimal.TryParse(Norm(row[cPrice]), NumberStyles.Any, CultureInfo.InvariantCulture, out price))
                    { res.Errors.Add($"{t.TableName} سطر {r + 1}: السعر غير صالح"); continue; }
                    var w = ResolveWh(Norm(row[cWh]), t.TableName, r + 1); if (w == null) continue;
                    if (!seen.Add(pn + "|" + w.Id)) { res.Errors.Add($"{t.TableName} سطر {r + 1}: القطعة «{pn}» مكررة بنفس المخزن"); continue; }
                    var d = cDesc >= 0 ? Norm(row[cDesc]) : "";
                    res.Parts.Add(new InventoryPart { PartNumber = pn, Description = d.Length > 400 ? d[..400] : d, Price = price, SystemQuantity = q,
                        SystemLocation = cLoc >= 0 ? Norm(row[cLoc]) is var l && l.Length > 0 ? (l.Length > 64 ? l[..64] : l) : null : null, WarehouseId = w.Id, BranchId = w.BranchId });
                }
            }
        }
        if (!any) res.Errors.Add("الملف ما بيه ورقة قطع غيار (عمود «رقم القطعة») أو ورقة مركبات (عمود «VIN»)");
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
        if (ext is not (".xlsx" or ".xls")) return OpResult.Fail("الملف لازم يكون .xlsx أو .xls");
        if (length <= 0) return OpResult.Fail("الملف فارغ");
        if (length > opt.Value.MaxUploadBytes) return OpResult.Fail("حجم الملف أكبر من المسموح");
        if (await db.Sessions.AnyAsync(x => x.Status == SessionStatus.InProgress)) return OpResult.Fail("توجد جلسة جرد قيد التنفيذ، أنهِها أولاً");
        if (await db.Sessions.AnyAsync(x => x.Year == year)) return OpResult.Fail($"توجد جلسة جرد لسنة {year}");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        ms.Position = 0;
        var whs = await db.Warehouses.AsNoTracking().ToListAsync();
        var res = importer.Read(ms, whs);
        if (res.Errors.Count > 0) return OpResult.Fail(string.Join("\n", res.Errors.Take(15)) + (res.Errors.Count > 15 ? $"\n… و{res.Errors.Count - 15} أخطاء ثانية" : ""));

        var stored = $"{Guid.NewGuid():N}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(Dir, stored), ms.ToArray());
        var session = new StocktakingSession { Year = year, StartedByAdminId = s.Id, StartedAt = DateTime.UtcNow, FileName = Path.GetFileName(fileName), StoredFileName = stored };
        foreach (var p in res.Parts) session.Parts.Add(p);
        foreach (var v in res.Vins) session.ExpectedVins.Add(v);
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
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
