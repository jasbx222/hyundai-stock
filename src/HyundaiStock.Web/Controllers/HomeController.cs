using HyundaiStock.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HyundaiStock.Web.Controllers;

public sealed class HomeController : DashboardController
{
    public IActionResult Index() => View();

    [AllowAnonymous]
    public IActionResult NotFoundPage() { Response.StatusCode = 404; return View("~/Views/Shared/NotFound.cshtml"); }

    [AllowAnonymous]
    public IActionResult Error() { Response.StatusCode = 500; return View("~/Views/Shared/Error.cshtml"); }
}
