using System;
using System.Configuration;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Routing;
using FeeBilling.Data;
using FeeBilling.Web.App_Start;
using log4net;

namespace FeeBilling.Web
{
    public class MvcApplication : HttpApplication
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(MvcApplication));

        protected void Application_Start()
        {
            log4net.Config.XmlConfigurator.Configure();

            AreaRegistration.RegisterAllAreas();
            GlobalConfiguration.Configure(WebApiConfig.Register);
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            UnityConfig.RegisterComponents();

            // Strangler-fig migration (2025): the ASP.NET Core services behind the YARP gateway call
            // back into this app to resolve the Forms-auth user (remote authentication).
            // Every request to Accounts.Api costs a round-trip to here.
            SystemWebAdapterConfiguration.AddSystemWebAdapters(this)
                .AddProxySupport(options => options.UseForwardedHeaders = true)
                .AddRemoteAppServer(options => options.ApiKey = ConfigurationManager.AppSettings["RemoteAppApiKey"])
                .AddAuthenticationServer();

            Log.Info("FeeBilling.Web started");
        }

        protected void Application_EndRequest(object sender, EventArgs e)
        {
            DbContextFactory.DisposeCurrent();
        }

        protected void Application_Error(object sender, EventArgs e)
        {
            var ex = Server.GetLastError();
            Log.Error("Unhandled exception on " + Request.Url, ex);
        }
    }
}
