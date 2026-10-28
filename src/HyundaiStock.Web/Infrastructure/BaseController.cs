using HyundaiStock.Infrastructure.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HyundaiStock.Web.Infrastructure;

[Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)]
[AutoValidateAntiforgeryToken]
public abstract class DashboardController : Controller
{
    protected AdminScope Scope => (AdminScope)HttpContext.Items[AdminScopeFilter.Key]!;

    protected IActionResult Done(OpResult r, string ok, string? returnUrl = null)
    {
        if (r.Ok) TempData["ok"] = ok; else TempData["err"] = r.Error;
        return Back(returnUrl);
    }

    protected IActionResult Back(string? returnUrl = null)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        var referer = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referer, UriKind.Absolute, out var u) && u.Host == Request.Host.Host) return LocalRedirect(u.PathAndQuery);
        return RedirectToAction("Index");
    }

    protected void AddErrors(Dictionary<string, string> errors)
    {
        foreach (var (k, v) in errors) ModelState.AddModelError(k, v);
    }

    protected IActionResult NotFoundPage() { Response.StatusCode = 404; return View("~/Views/Shared/NotFound.cshtml"); }
}
