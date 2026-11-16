using ClosedXML.Excel;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Services;
using HyundaiStock.Web.Infrastructure;
using HyundaiStock.Web.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace HyundaiStock.Web.Controllers;

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin, AdminRole.WarehouseManager, AdminRole.Accountant, AdminRole.AccountsManager)]
public sealed class RequestsController(QuantityRequestService svc, PartService parts, ListExports xl) : DashboardController
{
    public async Task<IActionResult> Index(string? search, string? kind, int? year, int page = 1)
    {
        ViewBag.Kind = kind == "recount" ? "recount" : "qty";
        if (kind == "recount") return View("Recounts", await parts.RecountsAsync(new PageQuery(page, 10, search), Scope, year));
        if (!Menu.QuantityTab(Scope)) return ForbidPage();
        return View(await svc.ListAsync(new PageQuery(page, 10, search), Scope, year));
    }

    public async Task<IActionResult> Export(string? search, string? kind, int? year)
    {
        if (kind == "recount") return Excel(await xl.RecountsAsync(search, year, Scope));
        if (!Menu.QuantityTab(Scope)) return ForbidPage();
        return Excel(await xl.QuantityRequestsAsync(search, year, Scope));
    }

    public async Task<IActionResult> Create()
    {
        if (!Menu.QuantityTab(Scope)) return ForbidPage();
        ViewBag.Choices = await svc.ChoicesAsync(Scope);
        return View("Form", new QtyInput());
    }

    [HttpPost]
    public async Task<IActionResult> Create(QtyInput input)
    {
        if (!Menu.QuantityTab(Scope)) return ForbidPage();
        var err = await svc.SaveAsync(null, input, Scope);
        if (err.Count == 0) { TempData["ok"] = "تم إرسال طلب التعديل"; return RedirectToAction(nameof(Index)); }
        AddErrors(err);
        ViewBag.Choices = await svc.ChoicesAsync(Scope);
        return View("Form", input);
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (!Menu.QuantityTab(Scope)) return ForbidPage();
        var r = await svc.GetAsync(id, Scope);
        if (r == null) return NotFoundPage();
        if (r.Status != RequestStatus.Pending || r.ApprovalLevel != ApprovalLevel.None) { TempData["err"] = "لا يمكن تعديل طلب بعد بدء الموافقة عليه"; return RedirectToAction(nameof(Index)); }
        ViewBag.Id = id;
        ViewBag.Choices = await svc.ChoicesAsync(Scope);
        return View("Form", new QtyInput
        {
            PartNumber = r.PartNumber, PartDescription = r.PartDescription, OldQuantity = r.OldQuantity, NewQuantity = r.NewQuantity, Reason = r.Reason,
            Audience = r.AudienceKind switch { 1 => "user", 2 => "branch", 3 => "warehouse", _ => null },
            AudienceUserId = r.AudienceUserId, AudienceBranchId = r.BranchId, AudienceWarehouseId = r.WarehouseId
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, QtyInput input)
    {
        if (!Menu.QuantityTab(Scope)) return ForbidPage();
        var err = await svc.SaveAsync(id, input, Scope);
        if (err.Count == 0) { TempData["ok"] = "تم تعديل الطلب"; return RedirectToAction(nameof(Index)); }
        AddErrors(err); ViewBag.Id = id;
        ViewBag.Choices = await svc.ChoicesAsync(Scope);
        return View("Form", input);
    }

    [HttpPost] public async Task<IActionResult> Approve(int id) => Menu.QuantityTab(Scope) ? Done(await svc.ApproveAsync(id, Scope), "تم قبول الطلب") : ForbidPage();
    [HttpPost] public async Task<IActionResult> FinalApprove(int id) => Menu.QuantityTab(Scope) ? Done(await svc.FinalApproveAsync(id, Scope), "تم القبول النهائي وتحديث الكمية") : ForbidPage();
    [HttpPost] public async Task<IActionResult> Reject(int id, string? reason) => Menu.QuantityTab(Scope) ? Done(await svc.RejectAsync(id, reason, Scope), "تم رفض الطلب") : ForbidPage();
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin, AdminRole.WarehouseManager, AdminRole.Accountant, AdminRole.AccountsManager, AdminRole.SparePartsManager, AdminRole.VehicleWarehouseManager)]
public sealed class PartsController(PartService svc, QuantityRequestService requests) : DashboardController
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
        ViewBag.Tab = Menu.QuantityTab(Scope) || tab != "corrections" ? tab : "recounts";
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
        return Done(result, "تم إرسال طلب إعادة العد", result.Ok ? "/Requests?kind=recount" : returnUrl);
    }

    public async Task<IActionResult> QuantityForm(int id)
    {
        if (!Menu.QuantityTab(Scope) || Scope.HidesStockFigures) return ForbidPage();
        var d = await svc.GetAsync(id, Scope);
        if (d == null) return NotFound();
        ViewBag.Users = await svc.CandidateUsersAsync(id);
        return PartialView("_Quantity", d.Part);
    }

    [HttpPost]
    public async Task<IActionResult> Quantity(int id, int? newQuantity, string? reason, string? audience, int? audienceUserId, int? audienceBranchId, int? audienceWarehouseId, string? returnUrl)
    {
        if (!Menu.QuantityTab(Scope) || Scope.HidesStockFigures) return ForbidPage();
        var err = await requests.SaveAsync(null, new QtyInput { InventoryPartId = id, NewQuantity = newQuantity, Reason = reason, Audience = audience, AudienceUserId = audienceUserId, AudienceBranchId = audienceBranchId, AudienceWarehouseId = audienceWarehouseId }, Scope);
        return Done(err.Count == 0 ? OpResult.Success() : OpResult.Fail(string.Join("\n", err.Values)), "تم إرسال طلب تحديث الكمية", returnUrl);
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
