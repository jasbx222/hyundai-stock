using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Services;
using HyundaiStock.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace HyundaiStock.Web.Controllers;

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.SparePartsManager, AdminRole.Accountant, AdminRole.AccountsManager)]
public sealed class NewPartsController(NewPartRequestService svc) : DashboardController
{
    public async Task<IActionResult> Index(string? search, string? status, int page = 1)
    {
        NewPartRequestStatus? st = Enum.TryParse<NewPartRequestStatus>(status, out var parsed) ? parsed : null;
        ViewBag.Status = status;
        return View(await svc.ListAsync(new PageQuery(page, 10, search), Scope, st));
    }

    public async Task<IActionResult> Details(int id)
    {
        var d = await svc.GetAsync(id, Scope);
        return d == null ? NotFoundPage() : View(d);
    }

    [HttpPost] public async Task<IActionResult> Approve(int id) => Done(await svc.ApproveAsync(id, Scope), "تمت الموافقة وتحويل الطلب للمالية");
    [HttpPost] public async Task<IActionResult> Return(int id, string? reason) => Done(await svc.ReturnAsync(id, reason, Scope), "تم إرجاع الطلب مع توضيح السبب");
    [HttpPost] public async Task<IActionResult> FinalApprove(int id) => Done(await svc.FinalApproveAsync(id, Scope), "تم التثبيت وإضافة القطعة للجرد");
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Accountant, AdminRole.AccountsManager)]
public sealed class FinanceController(ExportService exports, BranchService branches, WarehouseService warehouses, SessionLookup sessions) : DashboardController
{
    public async Task<IActionResult> Index([FromQuery] FinanceFilter f, string? search, int page = 1)
    {
        f.PartNumber ??= search;
        await Lookups();
        var (pageResult, error, forbidden) = await exports.FinanceListAsync(f, new PageQuery(page, 10, search), Scope);
        if (forbidden) { Response.StatusCode = 403; return View("~/Views/Shared/Forbidden.cshtml"); }
        if (error != null) { TempData["err"] = error; pageResult = new PagedResult<ExportService.FinanceRow>(); }
        ViewBag.Filter = f;
        return View(pageResult);
    }

    public async Task<IActionResult> Export([FromQuery] FinanceFilter f)
    {
        var (file, error, forbidden) = await exports.FinanceFileAsync(f, Scope);
        if (forbidden) { Response.StatusCode = 403; return View("~/Views/Shared/Forbidden.cshtml"); }
        if (file == null) { TempData["err"] = error; return RedirectToAction(nameof(Index), f); }
        return File(file.Content, ExportFile.ContentType, file.FileName);
    }

    private async Task Lookups()
    {
        ViewBag.Branches = await branches.AllAsync(Scope);
        ViewBag.Warehouses = await warehouses.AllAsync(Scope);
        ViewBag.Sessions = await sessions.OptionsAsync();
    }
}
