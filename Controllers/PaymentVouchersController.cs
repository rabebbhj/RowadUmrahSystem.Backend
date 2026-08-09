using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers
{
    public class PaymentVouchersController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
