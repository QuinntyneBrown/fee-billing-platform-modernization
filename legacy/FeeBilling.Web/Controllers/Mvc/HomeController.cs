using System.Web.Mvc;

namespace FeeBilling.Web.Controllers.Mvc
{
    [Authorize]
    public class HomeController : Controller
    {
        // Serves the AngularJS shell. Everything after this is client-side routing (#!/billing etc.).
        public ActionResult Index()
        {
            ViewBag.UserName = User.Identity.Name;
            return View();
        }
    }
}
