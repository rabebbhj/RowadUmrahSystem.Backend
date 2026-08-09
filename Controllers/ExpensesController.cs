using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers
{
    public class ExpensesController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
