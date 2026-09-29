using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWebHostEnvironment _environment;

        public HomeController(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public IActionResult Index()
        {
            var appIndexPath = Path.Combine(_environment.WebRootPath, "app", "index.html");
            return PhysicalFile(appIndexPath, "text/html; charset=utf-8");
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}
