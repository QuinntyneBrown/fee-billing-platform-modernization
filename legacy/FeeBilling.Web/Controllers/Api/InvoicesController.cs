using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using FeeBilling.Data;
using FeeBilling.Invoicing;

namespace FeeBilling.Web.Controllers.Api
{
    [RoutePrefix("api/invoices")]
    public class InvoicesController : ApiController
    {
        private readonly IInvoicePdfRenderer _pdfRenderer;

        public InvoicesController(IInvoicePdfRenderer pdfRenderer)
        {
            _pdfRenderer = pdfRenderer;
        }

        [HttpGet, Route("")]
        public IHttpActionResult GetInvoices(int runId)
        {
            var db = DbContextFactory.Current;
            var invoices = db.Invoices
                .Where(i => i.RunId == runId)
                .ToList()                               // no paging; 40k rows for the large firms
                .Select(i => new
                {
                    i.Id,
                    i.RunId,
                    i.AccountId,
                    i.Account.AccountNumber,            // lazy load per invoice
                    i.Account.AccountName,
                    i.HouseholdId,
                    i.Amount,
                    i.Status,
                    i.InvoiceDate,
                    i.ApprovedBy,
                    i.ApprovedOn
                });
            return Ok(invoices);
        }

        [HttpGet, Route("{id:int}")]
        public IHttpActionResult GetInvoice(int id)
        {
            var db = DbContextFactory.Current;
            var invoice = db.Invoices.Find(id);
            if (invoice == null) return NotFound();

            return Ok(new
            {
                invoice.Id,
                invoice.RunId,
                invoice.AccountId,
                invoice.Account.AccountNumber,
                invoice.Account.AccountName,
                invoice.HouseholdId,
                invoice.Amount,
                invoice.Status,
                invoice.InvoiceDate,
                PeriodEnd = invoice.BillingRun.PeriodEnd
            });
        }

        [HttpGet, Route("{id:int}/pdf")]
        public HttpResponseMessage GetInvoicePdf(int id)
        {
            var db = DbContextFactory.Current;
            var invoice = db.Invoices.Find(id);
            if (invoice == null) return Request.CreateResponse(HttpStatusCode.NotFound);

            var account = invoice.Account;
            var firm = account.Firm;
            var household = account.Household;
            var schedule = household != null && account.FeeScheduleId == null ? household.FeeSchedule : account.FeeSchedule;

            var pdf = _pdfRenderer.Render(new InvoiceDocument
            {
                InvoiceId = invoice.Id,
                FirmName = firm.Name,
                AccountNumber = account.AccountNumber,
                AccountName = account.AccountName,
                HouseholdCode = household != null ? household.HouseholdCode : null,
                PeriodEnd = invoice.BillingRun.PeriodEnd,
                Amount = invoice.Amount,
                ScheduleCode = schedule != null ? schedule.Code : null
            });

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pdf) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return response;
        }
    }
}
