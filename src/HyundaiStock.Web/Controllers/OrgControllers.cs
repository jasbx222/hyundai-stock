using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Services;
using HyundaiStock.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace HyundaiStock.Web.Controllers;

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin)]
public sealed class BranchesController(BranchService svc) : DashboardController
{
    public async Task<IActionResult> Index(string? search, int page = 1) => View(await svc.ListAsync(new PageQuery(page, 10, search), Scope));

    [HttpPost, Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin)]
    public async Task<IActionResult> Save(int? id, string? name, int? number) => Done(await svc.SaveAsync(id, name, number), id == null ? "تمت إضافة الفرع" : "تم تعديل الفرع");

    [HttpPost, Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin)]
    public async Task<IActionResult> Delete(int id) => Done(await svc.DeleteAsync(id), "تم حذف الفرع");
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin)]
public sealed class WarehousesController(WarehouseService svc, BranchService branches) : DashboardController
{
    public async Task<IActionResult> Index(string? search, int? branchId, int page = 1)
    {
        ViewBag.Branches = await branches.AllAsync(Scope);
        ViewBag.BranchId = branchId;
        return View(await svc.ListAsync(new PageQuery(page, 10, search), branchId, Scope));
    }

    [HttpPost, Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin)]
    public async Task<IActionResult> Save(int? id, string? name, int type, int branchId, int? number) => Done(await svc.SaveAsync(id, name, type, branchId, number), id == null ? "تمت إضافة المخزن" : "تم تعديل المخزن");

    [HttpPost, Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin)]
    public async Task<IActionResult> Delete(int id) => Done(await svc.DeleteAsync(id), "تم حذف المخزن");
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin)]
public sealed class AdminsController(AdminService svc, BranchService branches, WarehouseService warehouses) : DashboardController
{
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        ViewBag.Branches = await branches.AllAsync();
        return View(await svc.ListAsync(new PageQuery(page, 10, search), Scope));
    }

    public async Task<IActionResult> Create()
    {
        ViewBag.Branches = await branches.AllAsync();
        ViewBag.Warehouses = await warehouses.AllAsync();
        return View(new AdminInput());
    }

    [HttpPost]
    public async Task<IActionResult> Create(AdminInput input)
    {
        var err = await svc.CreateAsync(input, Scope);
        if (err.Count == 0) { TempData["ok"] = "تمت إضافة المشرف"; return RedirectToAction(nameof(Index)); }
        AddErrors(err);
        ViewBag.Branches = await branches.AllAsync();
        ViewBag.Warehouses = await warehouses.AllAsync();
        return View(input);
    }

    public async Task<IActionResult> Details(int id)
    {
        var a = await svc.GetAsync(id);
        if (a == null || (a.Role == AdminRole.MainAdmin && !Scope.IsMain)) return NotFound();
        return PartialView("_Details", a);
    }

    [HttpPost] public async Task<IActionResult> SetActive(int id, bool active) => Done(await svc.SetActiveAsync(id, active, Scope), active ? "تم تفعيل المشرف" : "تم تعطيل المشرف");
    [HttpPost] public async Task<IActionResult> ResetPassword(int id, string? password) => Done(await svc.ResetPasswordAsync(id, password, Scope), "تم تغيير كلمة المرور");
    [HttpPost] public async Task<IActionResult> AssignBranch(int id, int branchId) => Done(await svc.AssignBranchAsync(id, branchId, Scope), "تم تعيين الفرع");

    public async Task<IActionResult> Edit(int id)
    {
        var a = await svc.GetAsync(id);
        if (a == null || a.Role == AdminRole.MainAdmin || a.Id == Scope.Id) return NotFoundPage();
        var (bs, ws) = await svc.AssignmentsAsync(id);
        ViewBag.Branches = await branches.AllAsync();
        ViewBag.Warehouses = await warehouses.AllAsync();
        ViewBag.Id = id;
        return View("Create", new AdminInput { Name = a.Name, PhoneNumber = a.PhoneNumber, Role = a.Role, BranchId = a.BranchId, BranchIds = bs, WarehouseIds = ws });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, AdminInput input)
    {
        var err = await svc.UpdateAsync(id, input, Scope);
        if (err.Count == 0) { TempData["ok"] = "تم تعديل المشرف"; return RedirectToAction(nameof(Index)); }
        AddErrors(err);
        ViewBag.Branches = await branches.AllAsync();
        ViewBag.Warehouses = await warehouses.AllAsync();
        ViewBag.Id = id;
        return View("Create", input);
    }
}

[Roles(AdminRole.MainAdmin, AdminRole.SuperAdmin, AdminRole.Admin)]
public sealed class UsersController(UserService svc, BranchService branches, WarehouseService warehouses) : DashboardController
{
    private async Task Lookups()
    {
        ViewBag.Branches = await branches.AllAsync(Scope);
        ViewBag.Warehouses = await warehouses.AllAsync(Scope);
    }

    public async Task<IActionResult> Index(string? search, int page = 1) => View(await svc.ListAsync(new PageQuery(page, 10, search), Scope));

    public async Task<IActionResult> Create() { await Lookups(); return View("Form", new UserInput()); }

    [HttpPost]
    public async Task<IActionResult> Create(UserInput input)
    {
        var err = await svc.SaveAsync(null, input, Scope);
        if (err.Count == 0) { TempData["ok"] = "تمت إضافة المستخدم"; return RedirectToAction(nameof(Index)); }
        AddErrors(err); await Lookups();
        return View("Form", input);
    }

    public async Task<IActionResult> Edit(int id)
    {
        var u = await svc.GetAsync(id, Scope);
        if (u == null) return NotFoundPage();
        await Lookups(); ViewBag.Id = id;
        return View("Form", new UserInput { Name = u.Name, PhoneNumber = u.PhoneNumber, BranchIds = u.BranchIds, WarehouseIds = u.WarehouseIds });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, UserInput input)
    {
        var err = await svc.SaveAsync(id, input, Scope);
        if (err.Count == 0) { TempData["ok"] = "تم تعديل المستخدم"; return RedirectToAction(nameof(Index)); }
        AddErrors(err); await Lookups(); ViewBag.Id = id;
        return View("Form", input);
    }

    public async Task<IActionResult> Details(int id)
    {
        var u = await svc.GetAsync(id, Scope);
        return u == null ? NotFound() : PartialView("_Details", u);
    }

    [HttpPost] public async Task<IActionResult> SetActive(int id, bool active) => Done(await svc.SetActiveAsync(id, active, Scope), active ? "تم تفعيل المستخدم" : "تم تعطيل المستخدم");
}
