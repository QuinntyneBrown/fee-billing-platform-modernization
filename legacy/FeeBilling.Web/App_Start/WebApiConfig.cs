using System.Web.Http;
using Newtonsoft.Json;

namespace FeeBilling.Web.App_Start
{
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional });

            // JSON only. Default Newtonsoft contract resolver => PascalCase property names.
            // The AngularJS app is written against PascalCase (e.g. run.RunId, tier.AnnualRate).
            config.Formatters.Remove(config.Formatters.XmlFormatter);
            config.Formatters.JsonFormatter.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;

            // Everything under /api requires a Forms-auth cookie.
            config.Filters.Add(new AuthorizeAttribute());
        }
    }
}
