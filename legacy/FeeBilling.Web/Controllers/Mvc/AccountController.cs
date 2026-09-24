using System.Web.Mvc;
using System.Web.Security;
using FeeBilling.Web.Models;
using log4net;

namespace FeeBilling.Web.Controllers.Mvc
{
    // Forms authentication. (Not to be confused with Api/AccountsController, which is client accounts.)
    public class AccountController : Controller
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AccountController));

        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public ActionResult Login(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            if (!Membership.ValidateUser(model.UserName, model.Password))
            {
                Log.Warn("Failed login for " + model.UserName + " from " + Request.UserHostAddress);
                ModelState.AddModelError("", "Invalid user name or password.");
                return View(model);
            }

            FormsAuthentication.SetAuthCookie(model.UserName, model.RememberMe);
            Log.Info("User " + model.UserName + " logged in");

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
    }
}
