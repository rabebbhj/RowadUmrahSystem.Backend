using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers
{
    public class FinancialReportsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
