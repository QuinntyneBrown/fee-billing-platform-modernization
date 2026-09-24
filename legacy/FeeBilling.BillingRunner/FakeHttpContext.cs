using System.IO;
using System.Security.Principal;
using System.Web;

namespace FeeBilling.BillingRunner
{
    /// <summary>
    /// FeeCalculator and friends get their DbContext from HttpContext.Current.Items
    /// (DbContextFactory). Outside IIS there is no HttpContext, so we make one up.
    /// </summary>
    public static class FakeHttpContext
    {
        public static HttpContext Create()
        {
            var request = new HttpRequest(string.Empty, "http://localhost/", string.Empty);
            var response = new HttpResponse(new StringWriter());
            var context = new HttpContext(request, response)
            {
                User = new GenericPrincipal(new GenericIdentity("BillingRunner"), new string[0])
            };
            return context;
        }
    }
}
