using System;
using System.Data;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Web;
using System.Web.Http;
using FeeBilling.Core;
using FeeBilling.Data;
using FeeBilling.Web.Helpers;
using FeeBilling.Web.Models;

namespace FeeBilling.Web.Controllers.Api
{
    [RoutePrefix("api/billing")]
    public class BillingController : ApiController
    {
        [HttpPost, Route("runs")]
        public IHttpActionResult StartRun(BillingRunRequest req)
        {
            var user = HttpContext.Current.User.Identity.Name;
            var runId = BillingRunService.Enqueue(req.FirmId, req.PeriodEnd, user);
            return Ok(new { runId });   // double-click = two billing runs = double-billed clients
        }

        [HttpGet, Route("runs/{id:int}/invoices")]
        public HttpResponseMessage GetInvoices(int id, string format = "json")
        {
            DataSet ds = InvoiceRepository.GetInvoiceDataSet(id);   // ADO.NET, no paging
            if (format == "csv") return CsvResult(ds.Tables[0]);
            return Request.CreateResponse(HttpStatusCode.OK, ds);
        }

        [HttpGet, Route("runs")]
        public IHttpActionResult GetRuns(int firmId)
        {
            var db = DbContextFactory.Current;
            var runs = db.BillingRunQueue
                .Where(r => r.FirmId == firmId)
                .OrderByDescending(r => r.RequestedOn)
                .ToList()
                .Select(r => new
                {
                    r.Id,
                    r.FirmId,
                    r.PeriodEnd,
                    r.Status,
                    r.RequestedBy,
                    r.RequestedOn,
                    r.CompletedOn,
                    InvoiceCount = r.Invoices.Count,          // lazy load per run
                    TotalAmount = r.Invoices.Sum(i => i.Amount)
                });
            return Ok(runs);
        }

        [HttpGet, Route("runs/{id:int}")]
        public IHttpActionResult GetRun(int id)
        {
            var db = DbContextFactory.Current;
            var run = db.BillingRunQueue.Find(id);
            if (run == null) return NotFound();

            return Ok(new
            {
                run.Id,
                run.FirmId,
                run.PeriodEnd,
                run.Status,
                run.RequestedBy,
                run.RequestedOn,
                run.StartedOn,
                run.CompletedOn,
                run.ErrorMessage,
                InvoiceCount = run.Invoices.Count,
                TotalAmount = run.Invoices.Sum(i => i.Amount)
            });
        }

        [HttpPost, Route("runs/{id:int}/approve")]
        [Authorize(Roles = "BillingAdmin")]
        public IHttpActionResult ApproveRun(int id)
        {
            var db = DbContextFactory.Current;
            var run = db.BillingRunQueue.Find(id);
            if (run == null) return NotFound();
            if (run.Status != "Complete") return BadRequest("Run " + id + " is " + run.Status + ", not Complete.");

            // Invoice.Status is StoreGeneratedPattern=Computed in the EDMX (FB-311), so EF
            // silently ignores changes to it. Update with SQL instead.
            var user = HttpContext.Current.User.Identity.Name;
            var updated = db.Database.ExecuteSqlCommand(
                "UPDATE dbo.Invoices SET Status = 'Approved', ApprovedBy = @p0, ApprovedOn = GETDATE() WHERE RunId = @p1 AND Status = 'Draft'",
                user, id);

            return Ok(new { runId = id, approved = updated });
        }

        private HttpResponseMessage CsvResult(DataTable table)
        {
            var csv = CsvExporter.ToCsv(table);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(csv, Encoding.UTF8, "text/csv")
            };
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "invoices-" + DateTime.Now.ToString("yyyyMMdd") + ".csv"
            };
            return response;
        }
    }
}
