using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers
{
    public class FiscalYearsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
