using System.Web;

namespace FeeBilling.Data
{
    /// <summary>
    /// One FeeBillingEntities per HTTP request, stashed in HttpContext.Current.Items.
    /// Disposed in Global.asax Application_EndRequest.
    ///
    /// Anything that runs outside IIS (BillingRunner, tools, tests) has to fake an HttpContext
    /// first. See FeeBilling.BillingRunner/FakeHttpContext.cs.
    /// </summary>
    public static class DbContextFactory
    {
        private const string ItemsKey = "__FeeBillingEntities";

        public static FeeBillingEntities Current
        {
            get
            {
                var items = HttpContext.Current.Items;
                var db = items[ItemsKey] as FeeBillingEntities;
                if (db == null)
                {
                    db = new FeeBillingEntities();
                    items[ItemsKey] = db;
                }
                return db;
            }
        }

        public static void DisposeCurrent()
        {
            if (HttpContext.Current == null) return;

            var db = HttpContext.Current.Items[ItemsKey] as FeeBillingEntities;
            if (db != null)
            {
                db.Dispose();
                HttpContext.Current.Items.Remove(ItemsKey);
            }
        }
    }
}
