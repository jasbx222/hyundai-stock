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
/// Reads session workbooks (.xlsx or .xls). Two kinds of input are accepted, detected by their headers:
/// 1) Our template:
///    sheet «Parts»: Quantity | Description | Location | Serial | BranchNo.  (Serial = part number, BranchNo. = Branch.Number)
///    sheet «Vins»:  VIN | WarehouseNo. | VehicleType  (WarehouseNo. may be empty, VehicleType = vehicle model shown in the app)
/// 2) Raw reports exported from the Hyundai system, uploaded as they are:
///    «القطع الموجودة» report: a title block with «الفرع &lt;name&gt;», then a header row
///       (ك.الموجودة | المتوفر | … | الوصف | الموقع | M. Class | [II] رمز القطعة | [I] رمز القطعة | ص), page footers and a totals row.
///       The branch is matched by name against the branches in the system.
///    Vehicle stock report: header row with Engine No. | VIN | Model | Type (Specs) | … | Status | Branch | …
///       Vehicle type = Model + Type (Specs); the branch is matched by name when possible.
/// The header row is searched for within the first rows of each sheet, so title blocks above it are skipped.
/// </summary>
public sealed class ExcelImportService
{
    static ExcelImportService() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static readonly string[] PartsHeaders = { "Quantity", "Description", "Location", "Serial", "BranchNo." };
    public static readonly string[] VinsHeaders = { "VIN", "WarehouseNo.", "VehicleType" };

    private const int HeaderScanRows = 40;
    private static readonly string[] Serial = { "serial", "partnumber", "partno", "iرمزالقطعة", "رمزالقطعة", "رقمالقطعة" };
    private static readonly string[] Desc = { "description", "الوصف" };
    private static readonly string[] Qty = { "quantity", "qty", "systemquantity", "كالموجودة", "الكميةالموجودة", "الكمية" };
    private static readonly string[] Loc = { "location", "locationinstock", "الموقع" };
    private static readonly string[] BranchNo = { "branchno", "branchnumber" };
    private static readonly string[] BranchName = { "branch", "الفرع" };
    private static readonly string[] VinH = { "vin", "رقمالشاصي", "الشاصي" };
    private static readonly string[] WhNo = { "warehouseno", "warehousenumber", "warehouse" };
    private static readonly string[] TypeH = { "vehicletype", "type", "نوعالمركبة", "نوعالسيارة", "model" };
    private static readonly string[] ModelH = { "model" };
    private static readonly string[] SpecsH = { "typespecs" };

    /// <summary>Header key: lower-case letters/digits only, Arabic tatweel and alef/yaa/taa-marbuta forms unified.</summary>
    private static string Key(object? o) => Norm(new string((o?.ToString() ?? "").ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) && ch != '\u0640').ToArray()));

    private static string Norm(string v) => v.Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي').Replace('ة', 'ه');

    /// <summary>Branch name key for matching report titles («الفرع فرع النجف») to branch names («فرع النجف» / «النجف»).</summary>
    private static string BranchKey(string v)
    {
        var k = Key(v);
        foreach (var w in new[] { "الفرع", "فرع", "branch" }) if (k.StartsWith(w)) k = k[w.Length..];
        return k.StartsWith("ال") && k.Length > 4 ? k[2..] : k;
    }

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
        foreach (var n in names)
            for (int c = 0; c < header.Length; c++) if (Key(header[c]) == Norm(n)) return c;
        return -1;
    }

    private static string Cut(string v, int max) => v.Length > max ? v[..max] : v;

    private static List<(string name, List<object?[]> rows)>? Load(Stream stream)
    {
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
        catch (Exception) { return null; }
        return sheets;
    }

    public ImportResult Read(Stream stream, IReadOnlyList<Warehouse> warehouses, IReadOnlyList<Branch> branches)
        => Read(new[] { ("", stream) }, warehouses, branches);

    /// <summary>Reads one or more workbooks into one session (e.g. a parts report per branch plus the vehicles report).</summary>
    public ImportResult Read(IEnumerable<(string fileName, Stream stream)> files, IReadOnlyList<Warehouse> warehouses, IReadOnlyList<Branch> branches)
    {
        var res = new ImportResult();
        var brByNo = branches.Where(x => x.Number != null).ToDictionary(x => x.Number!.Value);
        var whByNo = warehouses.Where(x => x.Number != null).ToDictionary(x => x.Number!.Value);
        var brByName = new Dictionary<string, Branch>();
        foreach (var b in branches) brByName.TryAdd(BranchKey(b.Name), b);
        var seenVins = new HashSet<string>();
        bool any = false;
        foreach (var (fileName, stream) in files)
        {
            var sheets = Load(stream);
            var fl = fileName.Length > 0 ? $"{fileName}: " : "";
            if (sheets == null) { res.Errors.Add($"{fl}الملف ليس ملف Excel صالحًا (.xlsx أو .xls)"); continue; }
            foreach (var (sheetName, rows) in sheets)
            {
                var name = fl + sheetName;
                // Header row: first row (within the title block) that holds a VIN column or a part-number column.
                int hr = -1, cVin = -1, cSerial = -1;
                for (int r = 0; r < Math.Min(HeaderScanRows, rows.Count) && hr < 0; r++)
                {
                    cVin = Find(rows[r], VinH); cSerial = Find(rows[r], Serial);
                    if (cVin >= 0 || cSerial >= 0) hr = r;
                }
                if (hr < 0) continue;
                var h = rows[hr];
                bool isVins = Key(sheetName) == "vins" || (cVin >= 0 && cSerial < 0);
                if (isVins)
                {
                    any = true;
                    int cWh = Find(h, WhNo), cModel = Find(h, ModelH), cSpecs = Find(h, SpecsH), cType = Find(h, TypeH), cBrName = Find(h, BranchName);
                    if (cSpecs >= 0 && cType == cSpecs) cType = -1;
                    for (int r = hr + 1; r < rows.Count; r++)
                    {
                        var row = rows[r];
                        // VINs in the system file sometimes carry a trailing marker such as «*»: keep letters/digits only.
                        var vin = new string(Text(row[cVin]).ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
                        if (vin.Length == 0 || Key(row[cVin]) == Key(h[cVin])) continue; // blank rows, repeated headers
                        if (!Validators.IsVin(vin)) { res.Errors.Add($"{name} سطر {r + 1}: VIN غير صالح «{vin}»"); continue; }
                        if (!seenVins.Add(vin)) continue; // repeated VIN: import once
                        Warehouse? w = null; Branch? br = null;
                        var wt = cWh >= 0 ? Text(row[cWh]) : "";
                        if (wt.Length > 0)
                        {
                            if (!TryInt(row[cWh], out var wn)) { res.Errors.Add($"{name} سطر {r + 1}: رقم المخزن غير صالح «{wt}»"); continue; }
                            if (!whByNo.TryGetValue(wn, out w)) { res.Errors.Add($"{name} سطر {r + 1}: المخزن رقم {wn} غير موجود في النظام"); continue; }
                        }
                        else if (cBrName >= 0 && Text(row[cBrName]) is { Length: > 0 } bn) brByName.TryGetValue(BranchKey(bn), out br);
                        string type;
                        if (cModel >= 0 || cSpecs >= 0)
                            type = string.Join(" - ", new[] { cModel >= 0 ? Text(row[cModel]) : "", cSpecs >= 0 ? Text(row[cSpecs]) : "" }.Where(x => x.Length > 0));
                        else type = cType >= 0 ? Text(row[cType]) : "";
                        res.Vins.Add(new ExpectedVin { Vin = vin, Model = type.Length > 0 ? Cut(type, 100) : null, WarehouseId = w?.Id, BranchId = w?.BranchId ?? br?.Id });
                    }
                }
                else
                {
                    any = true;
                    int cDesc = Find(h, Desc), cQty = Find(h, Qty), cLoc = Find(h, Loc), cBr = Find(h, BranchNo);
                    if (cQty < 0) { res.Errors.Add($"ورقة {name}: عمود الكمية («Quantity» أو «ك.الموجودة») مفقود"); continue; }
                    // Raw report: no BranchNo. column, the branch name is in the title block («الفرع فرع النجف»).
                    Branch? sheetBranch = null;
                    if (cBr < 0)
                    {
                        string? title = null;
                        for (int r = 0; r < hr && title == null; r++)
                            foreach (var c in rows[r])
                                if (Text(c) is var t && t.StartsWith("الفرع") && t.Length > 5) { title = t[5..].Trim(); break; }
                        if (title == null) { res.Errors.Add($"ورقة {name}: لا يوجد عمود «BranchNo.» ولا اسم الفرع في عنوان التقرير"); continue; }
                        if (!brByName.TryGetValue(BranchKey(title), out sheetBranch))
                        { res.Errors.Add($"ورقة {name}: الفرع «{title}» غير موجود في النظام، أضفه من صفحة الفروع بنفس الاسم"); continue; }
                    }
                    for (int r = hr + 1; r < rows.Count; r++)
                    {
                        var row = rows[r]; var pn = Text(row[cSerial]);
                        if (row.All(x => Text(x).Length == 0)) continue;
                        if (Key(row[cSerial]) == Key(h[cSerial])) continue; // header repeated on each printed page
                        // Raw report page footers («صفحة 1 من 14») and totals row («المجموع» / «العدد الكلي للقطع»).
                        if (cBr < 0 && (pn.Length == 0 || row.Any(x => Text(x) is var t && (t == "المجموع" || t.StartsWith("العدد الكلي") || t.StartsWith("صفحة "))))) continue;
                        if (pn.Length == 0 || pn.Length > 64) { res.Errors.Add($"{name} سطر {r + 1}: رقم القطعة غير صالح"); continue; }
                        int q = 0;
                        if (Text(row[cQty]).Length > 0 && !TryInt(row[cQty], out q)) { res.Errors.Add($"{name} سطر {r + 1}: الكمية غير صالحة"); continue; }
                        if (q < 0) { res.Errors.Add($"{name} سطر {r + 1}: الكمية سالبة"); continue; }
                        var br = sheetBranch;
                        if (br == null)
                        {
                            if (!TryInt(row[cBr], out var bn)) { res.Errors.Add($"{name} سطر {r + 1}: رقم الفرع (BranchNo.) غير صالح"); continue; }
                            if (!brByNo.TryGetValue(bn, out br)) { res.Errors.Add($"{name} سطر {r + 1}: الفرع رقم {bn} غير موجود في النظام"); continue; }
                        }
                        var loc = cLoc >= 0 ? Text(row[cLoc]) : "";
                        res.Parts.Add(new InventoryPart { PartNumber = pn, Description = cDesc >= 0 ? Cut(Text(row[cDesc]), 400) : "", SystemQuantity = q,
                            SystemLocation = loc.Length > 0 ? Cut(loc, 64) : null, BranchId = br.Id });
                    }
                }
            }
        }
        if (!any && res.Errors.Count == 0) res.Errors.Add("لم يُعثر على بيانات: ارفع قالب الجرد (ورقة Parts وورقة Vins) أو تقارير النظام (القطع الموجودة / المركبات)");
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

    public Task<OpResult> StartAsync(int year, Stream file, string fileName, long length, AdminScope s)
        => StartAsync(year, new[] { (file, fileName, length) }, s);

    /// <summary>Starts a session from one or more files (our template, or the system's raw reports as exported).</summary>
    public async Task<OpResult> StartAsync(int year, IReadOnlyList<(Stream file, string fileName, long length)> files, AdminScope s)
    {
        if (!s.IsSuper) return OpResult.Fail("بدء الجلسة للسوبر أدمن فقط");
        if (year < 2000 || year > 2100) return OpResult.Fail("السنة غير صالحة");
        if (files.Count == 0) return OpResult.Fail("اختر ملف Excel");
        foreach (var (_, fileName, length) in files)
        {
            var e = Path.GetExtension(fileName).ToLowerInvariant();
            if (e is not (".xlsx" or ".xls")) return OpResult.Fail($"{fileName}: يجب أن يكون الملف بصيغة .xlsx أو .xls");
            if (length <= 0) return OpResult.Fail($"{fileName}: الملف فارغ");
        }
        if (files.Sum(x => x.length) > opt.Value.MaxUploadBytes) return OpResult.Fail("حجم الملفات أكبر من المسموح");
        if (await db.Sessions.AnyAsync(x => x.Status == SessionStatus.InProgress)) return OpResult.Fail("توجد جلسة جرد قيد التنفيذ، أنهِها أولاً");
        if (await db.Sessions.AnyAsync(x => x.Year == year)) return OpResult.Fail($"توجد جلسة جرد لسنة {year}");

        var buffers = new List<(string name, byte[] data)>();
        foreach (var (file, fileName, _) in files)
        {
            using var m = new MemoryStream();
            await file.CopyToAsync(m);
            buffers.Add((Path.GetFileName(fileName), m.ToArray()));
        }
        var whs = await db.Warehouses.AsNoTracking().ToListAsync();
        var brs = await db.Branches.AsNoTracking().ToListAsync();
        var res = importer.Read(buffers.Select(b => (files.Count > 1 ? b.name : "", (Stream)new MemoryStream(b.data))).ToList(), whs, brs);
        if (res.Errors.Count > 0) return OpResult.Fail(string.Join("\n", res.Errors.Take(15)) + (res.Errors.Count > 15 ? $"\n… و{res.Errors.Count - 15} أخطاء أخرى" : ""));

        string stored, shownName;
        if (buffers.Count == 1)
        {
            stored = $"{Guid.NewGuid():N}{Path.GetExtension(buffers[0].name).ToLowerInvariant()}";
            await File.WriteAllBytesAsync(Path.Combine(Dir, stored), buffers[0].data);
            shownName = buffers[0].name;
        }
        else
        {
            // Several reports: keep them together as one zip so the session file download returns all of them.
            stored = $"{Guid.NewGuid():N}.zip";
            using (var zs = File.Create(Path.Combine(Dir, stored)))
            using (var zip = new System.IO.Compression.ZipArchive(zs, System.IO.Compression.ZipArchiveMode.Create))
            {
                var used = new HashSet<string>();
                foreach (var (n, d) in buffers)
                {
                    var en = n; for (int i = 2; !used.Add(en); i++) en = $"{Path.GetFileNameWithoutExtension(n)}-{i}{Path.GetExtension(n)}";
                    using var es = zip.CreateEntry(en).Open();
                    await es.WriteAsync(d);
                }
            }
            shownName = $"session-{year}-files.zip";
        }
        var session = new StocktakingSession { Year = year, StartedByAdminId = s.Id, StartedAt = DateTime.UtcNow, FileName = shownName.Length > 260 ? shownName[..260] : shownName, StoredFileName = stored };
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
        await using var tx = await db.Database.BeginTransactionAsync();
        var e = await db.Sessions.FindAsync(id);
        if (e == null) return OpResult.Fail("الجلسة غير موجودة");
        if (e.Status != SessionStatus.InProgress) return OpResult.Fail("الجلسة منتهية");
        e.Status = SessionStatus.Completed; e.EndedAt = DateTime.UtcNow; e.EndedByAdminId = s.Id;
        var now = DateTime.UtcNow;
        var open = await db.NewPartRequests.Where(x => x.SessionId == id &&
            (x.Status == NewPartRequestStatus.PendingSparePartsManager || x.Status == NewPartRequestStatus.PendingAccountant || x.Status == NewPartRequestStatus.ReturnedToEmployee)).ToListAsync();
        foreach (var r in open)
        {
            var prev = r.Status;
            r.Status = NewPartRequestStatus.SessionClosed; r.UpdatedAt = now;
            db.WorkflowAudits.Add(new WorkflowAudit
            {
                Subject = AuditActions.NewPart, SubjectId = r.Id, NewPartRequestId = r.Id, Action = AuditActions.SessionClosed,
                FromStatus = prev.ToString(), ToStatus = nameof(NewPartRequestStatus.SessionClosed), ActorAdminId = s.Id, ActorRole = s.Role.ToString(),
                SessionId = r.SessionId, WarehouseId = r.WarehouseId, BranchId = r.BranchId
            });
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
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
