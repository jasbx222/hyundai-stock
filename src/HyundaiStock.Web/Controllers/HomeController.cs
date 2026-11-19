using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using HyundaiStock.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HyundaiStock.Web.Controllers;

public sealed class HomeController(AppDbContext db, SessionLookup sessions) : DashboardController
{
    public async Task<IActionResult> Index(int? sessionId) => View(await HomeStats.BuildAsync(db, sessions, Scope, sessionId));

    [AllowAnonymous]
    public IActionResult NotFoundPage() { Response.StatusCode = 404; return View("~/Views/Shared/NotFound.cshtml"); }

    [AllowAnonymous]
    public IActionResult Error() { Response.StatusCode = 500; return View("~/Views/Shared/Error.cshtml"); }
}
