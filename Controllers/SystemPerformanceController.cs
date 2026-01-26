using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace UniCP.Controllers
{
    [Authorize(Roles = "SystemPerformance,Admin")]
    public class SystemPerformanceController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Sistem Performansı";
            return View();
        }
    }
}
