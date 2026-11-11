using System.Security.Claims;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Web.Infrastructure;

/// <summary>Loads the signed-in admin from the database on every dashboard request, so role, branch and active state are always current.</summary>
public sealed class AdminScopeFilter(AppDbContext db) : IAsyncActionFilter
{
    public const string Key = "AdminScope";
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var http = ctx.HttpContext;
        if (http.User.Identity?.IsAuthenticated == true && http.User.Identity.AuthenticationType == CookieAuthenticationDefaults.AuthenticationScheme
            && int.TryParse(http.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            var a = await db.Admins.AsNoTracking().Include(x => x.Branches).Include(x => x.Warehouses).FirstOrDefaultAsync(x => x.Id == id);
            if (a == null || !a.IsActive)
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                ctx.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }
            http.Items[Key] = new AdminScope(a.Id, a.Name, a.Role, a.BranchId, a.Warehouses.Select(x => x.WarehouseId).ToList(), a.Branches.Select(x => x.BranchId).ToList(), a.AllBranches);
        }
        await next();
    }
}

/// <summary>Restricts a dashboard action/controller to the given roles (checked against the fresh DB role).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RolesAttribute(params AdminRole[] roles) : Attribute, IAsyncActionFilter, IOrderedFilter
{
    public int Order => 10;
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (ctx.HttpContext.Items[AdminScopeFilter.Key] is not AdminScope s || !roles.Contains(s.Role))
        {
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Result = new ViewResult { ViewName = "~/Views/Shared/Forbidden.cshtml", StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }
}

public static class Menu
{
    public static bool Users(AdminScope s) => s.Role is AdminRole.MainAdmin or AdminRole.SuperAdmin or AdminRole.Admin;
    public static bool Admins(AdminScope s) => s.IsSuper;
    public static bool Sessions(AdminScope s) => s.IsSuper;
    public static bool VinRequests(AdminScope s) => s.Role is AdminRole.Admin or AdminRole.WarehouseManager or AdminRole.VehicleWarehouseManager;
    public static bool ManageOrg(AdminScope s) => s.IsSuper;
    public static bool Org(AdminScope s) => s.Role is AdminRole.MainAdmin or AdminRole.SuperAdmin or AdminRole.Admin;
    public static bool Parts(AdminScope s) => true;
    public static bool Requests(AdminScope s) => s.Role is not (AdminRole.SparePartsManager or AdminRole.VehicleWarehouseManager);
    public static bool QuantityTab(AdminScope s) => s.Role == AdminRole.MainAdmin;
    public static bool Cars(AdminScope s) => s.Role != AdminRole.SparePartsManager;
    public static bool NewParts(AdminScope s) => s.CanReviewNewPart || s.CanFinalApproveNewPart;
    public static bool Finance(AdminScope s) => s.Role is AdminRole.MainAdmin or AdminRole.SuperAdmin or AdminRole.Accountant or AdminRole.AccountsManager;
    public static bool PartActions(AdminScope s) => s.CanFirstApproveQty;
    public static bool LegacyExport(AdminScope s) => s.Role is AdminRole.MainAdmin or AdminRole.SuperAdmin or AdminRole.Admin;
}

public static class ClaimsBuilder
{
    public static ClaimsPrincipal ForAdmin(Admin a)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, a.Id.ToString()),
            new(ClaimTypes.Name, a.Name),
            new(ClaimTypes.Role, ((int)a.Role).ToString())
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
