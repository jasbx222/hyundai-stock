using ClosedXML.Excel;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Services;
using HyundaiStock.Web.Infrastructure;
using HyundaiStock.Web.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace HyundaiStock.Web.Controllers;

/// <summary>طلبات إعادة العد: recount tasks an admin sent to a counter, and their result. Quantity-correction requests were removed.</summary>
[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin, AdminRole.WarehouseManager, AdminRole.Accountant, AdminRole.AccountsManager)]
public sealed class RequestsController(PartService parts, ListExports xl) : DashboardController
{
    public async Task<IActionResult> Index(string? search, int? year, int page = 1) =>
        View("Recounts", await parts.RecountsAsync(new PageQuery(page, 10, search), Scope, year));

    public async Task<IActionResult> Export(string? search, int? year) => Excel(await xl.RecountsAsync(search, year, Scope));
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin, AdminRole.WarehouseManager, AdminRole.Accountant, AdminRole.AccountsManager, AdminRole.SparePartsManager, AdminRole.VehicleWarehouseManager)]
public sealed class PartsController(PartService svc) : DashboardController
{
    public async Task<IActionResult> Index(string? search, string? view, int? year, int page = 1)
    {
        var selected = view is "counted" or "diff" ? view : null;
        var (p, session) = await svc.ListAsync(new PageQuery(page, 10, search), Scope, selected, year);
        ViewBag.Session = session;
        ViewBag.View = selected;
        return View(p);
    }

    public async Task<IActionResult> Details(int id, string? tab)
    {
        var d = await svc.GetAsync(id, Scope);
        if (d == null) return NotFoundPage();
        ViewBag.Tab = tab;
        return View(d);
    }

    public async Task<IActionResult> Info(int id)
    {
        var d = await svc.GetAsync(id, Scope);
        if (d == null) return NotFound();
        ViewBag.Indicator = d.Indicator;
        return PartialView("_Info", d.Part);
    }

    public async Task<IActionResult> RecountForm(int id)
    {
        if (!Scope.CanRequestRecount) return ForbidPage();
        var d = await svc.GetAsync(id, Scope);
        if (d == null) return NotFound();
        ViewBag.Users = await svc.CandidateUsersAsync(id);
        return PartialView("_Recount", d.Part);
    }

    [HttpPost]
    public async Task<IActionResult> Recount(int id, string mode, int? userId, string? returnUrl)
    {
        if (!Scope.CanRequestRecount) return ForbidPage();
        var target = mode == "other" ? (userId is > 0 ? userId : -1) : null;
        var result = await svc.RequestRecountAsync(id, target, Scope);
        return Done(result, "تم إرسال طلب إعادة العد", result.Ok ? "/Requests" : returnUrl);
    }
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin, AdminRole.WarehouseManager, AdminRole.Accountant, AdminRole.AccountsManager, AdminRole.VehicleWarehouseManager)]
public sealed class CarsController(VinService svc, WarehouseService warehouses, BranchService branches, AdminService admins, UserService users) : DashboardController
{
    public async Task<IActionResult> Index(string? search, int? year, string? view, int page = 1)
    {
        if (view == "missing")
        {
            ViewBag.View = view;
            return View("Missing", await svc.MissingAsync(new PageQuery(page, 10, search), Scope, year));
        }
        ViewBag.Branches = await branches.AllAsync(Scope);
        ViewBag.Users = await users.AllAsync(Scope);
        ViewBag.Admins = Scope.IsSuper ? (await admins.ListAsync(new PageQuery(1, 100), Scope)).Items.Select(a => new NamedItem(a.Id, a.Name)).ToList() : new List<NamedItem> { new(Scope.Id, Scope.Name) };
        return View(await svc.ListAsync(new PageQuery(page, 10, search), Scope, year));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var v = await svc.GetAsync(id, Scope);
        if (v == null) return NotFoundPage();
        ViewBag.Warehouses = await warehouses.AllAsync(Scope);
        return View(v);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, string? vin, int warehouseId)
    {
        var err = await svc.UpdateAsync(id, vin, warehouseId, Scope);
        if (err.Count == 0) { TempData["ok"] = "تم تعديل المركبة"; return RedirectToAction(nameof(Index)); }
        var v = await svc.GetAsync(id, Scope);
        if (v == null) return NotFoundPage();
        AddErrors(err);
        ViewBag.Warehouses = await warehouses.AllAsync(Scope);
        ViewBag.Vin = vin; ViewBag.WarehouseId = warehouseId;
        return View(v);
    }
}

[Roles(AdminRole.Admin, AdminRole.WarehouseManager, AdminRole.VehicleWarehouseManager)]
public sealed class VinRequestsController(VinRequestService svc, ListExports xl) : DashboardController
{
    public async Task<IActionResult> Index(string? search, int? year, int page = 1) => View(await svc.ListAsync(new PageQuery(page, 10, search), Scope, year));
    public async Task<IActionResult> Export(string? search, int? year) => Excel(await xl.VinRequestsAsync(search, year, Scope));
    [HttpPost] public async Task<IActionResult> Approve(int id) => Done(await svc.DecideAsync(id, true, Scope), "تمت الموافقة على تصحيح VIN");
    [HttpPost] public async Task<IActionResult> Reject(int id) => Done(await svc.DecideAsync(id, false, Scope), "تم رفض طلب تصحيح VIN");
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.AccountsManager)]
public sealed class SessionsController(SessionService svc, ListExports xl) : DashboardController
{
    public async Task<IActionResult> Index(string? search, int page = 1) => View(await svc.ListAsync(new PageQuery(page, 10, search)));
    public async Task<IActionResult> Export(string? search) => Excel(await xl.SessionsAsync(search));

    [HttpPost]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Start(int? year, List<IFormFile>? file)
    {
        if (!ModelState.IsValid) return Done(OpResult.Fail("السنة غير صالحة، أدخل سنة بين 2000 و2100"), "");
        var picked = (file ?? new()).Where(f => f.Length > 0).ToList();
        if (picked.Count == 0) return Done(OpResult.Fail("اختر ملف Excel"), "");
        var streams = picked.Select(f => f.OpenReadStream()).ToList();
        try
        {
            var input = picked.Select((f, i) => (streams[i], f.FileName, f.Length)).ToList();
            return Done(await svc.StartAsync(year ?? Ui.Local(DateTime.UtcNow).Year, input, Scope), "تم بدء جلسة الجرد");
        }
        finally { foreach (var st in streams) await st.DisposeAsync(); }
    }

    [HttpPost, Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin)] public async Task<IActionResult> End(int id) => Done(await svc.EndAsync(id, Scope), "تم إنهاء الجلسة");

    public async Task<IActionResult> File(int id)
    {
        var f = await svc.FileAsync(id);
        if (f == null) return NotFoundPage();
        var ct = f.Value.path.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ? "application/vnd.ms-excel"
            : f.Value.path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip" : ExportFile.ContentType;
        return PhysicalFile(f.Value.path, ct, f.Value.name);
    }

    /// <summary>Downloadable sample workbook with the expected columns.</summary>
    public IActionResult Template() => File(ExportService.ImportTemplate(), ExportFile.ContentType, "session-import-template.xlsx");
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin)]
public sealed class ExportsController(ExportService svc) : DashboardController
{
    private FileContentResult Send(ExportFile f) => File(f.Content, ExportFile.ContentType, f.FileName);

    /// <summary>kind: all | counted | uncounted</summary>
    public async Task<IActionResult> Parts(string kind, [FromQuery] ExportFilter f)
    {
        var k = kind switch { "counted" => PartExportKind.Counted, "uncounted" => PartExportKind.Uncounted, _ => PartExportKind.All };
        return Send(await svc.PartsAsync(k, f, Scope));
    }

    public async Task<IActionResult> Cars([FromQuery] ExportFilter f, bool all = false) => Send(await svc.ScannedVinsAsync(f, Scope, all));
    public async Task<IActionResult> Vins([FromQuery] ExportFilter f, bool missing = false) => Send(await svc.ExpectedVinsAsync(f, Scope, missing));
    public async Task<IActionResult> Branches() => Send(await svc.BranchesAsync(Scope));
    public async Task<IActionResult> Warehouses(int? branchId) => Send(await svc.WarehousesAsync(branchId, Scope));
}
