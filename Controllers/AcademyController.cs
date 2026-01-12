using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace UniCP.Controllers
{
    [Authorize]
    public class AcademyController : Controller
    {
        public IActionResult Index()
        {
            ViewData["Title"] = "Univera Akademi";
            return View();
        }
    }
}
