using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers
{
    public class CurrenciesController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
