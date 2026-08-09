using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers
{
    public class BankAccountsController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
