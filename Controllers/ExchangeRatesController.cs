using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers
{
    public class ExchangeRatesController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
