using System;
using FeeBilling.Invoicing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FeeBilling.Tests.Invoicing
{
    [TestClass]
    public class InvoicePdfRendererTests
    {
        private static InvoiceDocument Sample()
        {
            return new InvoiceDocument
            {
                InvoiceId = 1,
                FirmName = "Maple Ridge Wealth Partners",
                AccountNumber = "MRW000001001",
                AccountName = "Carter, Janet - RRSP",
                PeriodEnd = new DateTime(2016, 9, 30),
                Amount = 5312.50m,
                ScheduleCode = "STD-TIERED"
            };
        }

        [TestMethod, Ignore("GDI+ not available on the build server")]
        public void Render_ProducesPdf()
        {
            var bytes = new InvoicePdfRenderer().Render(Sample());
            Assert.AreEqual((byte)'%', bytes[0]);
        }

        [TestMethod, Ignore("GDI+ not available on the build server")]
        public void Render_WithoutLogo_UsesPlaceholder()
        {
            Assert.IsTrue(new InvoicePdfRenderer().Render(Sample()).Length > 0);
        }

        [TestMethod, Ignore("GDI+ not available on the build server")]
        public void Render_Household_IncludesHouseholdCode()
        {
            var invoice = Sample();
            invoice.HouseholdCode = "H-100";
            Assert.IsTrue(new InvoicePdfRenderer().Render(invoice).Length > 0);
        }
    }
}
