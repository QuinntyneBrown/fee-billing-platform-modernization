using System.Web.Http;
using System.Web.Mvc;
using FeeBilling.Data;
using FeeBilling.Invoicing;
using Unity;
using Unity.Injection;
using Unity.Lifetime;

namespace FeeBilling.Web.App_Start
{
    public static class UnityConfig
    {
        public static IUnityContainer Container { get; private set; }

        public static void RegisterComponents()
        {
            var container = new UnityContainer();

            // Newer controllers take FeeBillingEntities in the constructor; older code uses
            // DbContextFactory.Current. They are NOT the same instance within a request.
            container.RegisterType<FeeBillingEntities>(new HierarchicalLifetimeManager(), new InjectionConstructor());
            container.RegisterType<IInvoicePdfRenderer, InvoicePdfRenderer>();

            Container = container;
            DependencyResolver.SetResolver(new Unity.AspNet.Mvc.UnityDependencyResolver(container));
            GlobalConfiguration.Configuration.DependencyResolver = new Unity.AspNet.WebApi.UnityHierarchicalDependencyResolver(container);
        }
    }
}
