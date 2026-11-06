using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using HyundaiStock.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Web.Controllers;

public sealed class LoginVm { public string? PhoneNumber { get; set; } public string? Password { get; set; } public string? ReturnUrl { get; set; } }

[AllowAnonymous]
[AutoValidateAntiforgeryToken]
public sealed class AccountController(AppDbContext db, IPasswordService pw) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        return View(new LoginVm { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginVm vm)
    {
        var phone = AccountPhones.Normalize(vm.PhoneNumber);
        if (string.IsNullOrEmpty(phone)) ModelState.AddModelError(nameof(vm.PhoneNumber), "رقم الهاتف مطلوب");
        if (string.IsNullOrEmpty(vm.Password)) ModelState.AddModelError(nameof(vm.Password), "كلمة المرور مطلوبة");
        if (!ModelState.IsValid) return View(vm);
        var candidates = await db.Admins.AsNoTracking().ToListAsync();
        var matches = candidates.Where(x => AccountPhones.Normalize(x.PhoneNumber) == phone).ToList();
        var a = matches.Count == 1 ? matches[0] : null;
        if (a == null || !pw.Verify(a.PasswordHash, vm.Password!))
        {
            ModelState.AddModelError("", "رقم الهاتف أو كلمة المرور غير صحيحة");
            return View(vm);
        }
        if (!a.IsActive)
        {
            ModelState.AddModelError("", "الحساب معطّل، راجع المسؤول");
            return View(vm);
        }
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ClaimsBuilder.ForAdmin(a));
        if (!string.IsNullOrEmpty(vm.ReturnUrl) && Url.IsLocalUrl(vm.ReturnUrl)) return LocalRedirect(vm.ReturnUrl);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
