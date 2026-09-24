using System;
using System.Configuration;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Timers;
using System.Transactions;
using System.Web;
using FeeBilling.Core;
using FeeBilling.Data;
using FeeBilling.Data.Reporting;
using log4net;

namespace FeeBilling.BillingRunner
{
    public class BillingRunnerService : ServiceBase
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(BillingRunnerService));

        private readonly FeeBillingEntities _db = new FeeBillingEntities();
        private readonly ReportingEntities _reportingDb = new ReportingEntities();
        private HttpContext _fakeContext;
        private Timer _timer;

        public BillingRunnerService()
        {
            ServiceName = "FeeBilling.BillingRunner";
        }

        protected override void OnStart(string[] args)
        {
            HttpContext.Current = FakeHttpContext.Create();   // so FeeCalculator works outside IIS
            _fakeContext = HttpContext.Current;
            _timer = new Timer(30_000);
            _timer.Elapsed += (s, e) => ProcessQueue();
            _timer.Start();
            Log.Info("BillingRunner started");
        }

        protected override void OnStop()
        {
            if (_timer != null) _timer.Stop();
            Log.Info("BillingRunner stopped");
        }

        internal void StartInteractive(string[] args) { OnStart(args); }

        internal void StopInteractive() { OnStop(); }

        private void ProcessQueue()
        {
            HttpContext.Current = _fakeContext;   // timer threads don't inherit HttpContext

            var run = _db.BillingRunQueue.FirstOrDefault(r => r.Status == "Pending");
            if (run == null) return;

            Log.Info("Processing billing run " + run.Id + " for firm " + run.FirmId + ", period end " + run.PeriodEnd.ToShortDateString());

            var accounts = _db.Accounts.Where(a => a.FirmId == run.FirmId).ToList();
            using (var scope = new TransactionScope())          // Billing DB + Reporting DB → MSDTC
            {
                Parallel.ForEach(accounts.Where(a => !BillingRunService.IsHouseholdBilled(a)), acct =>   // shared DbContext across threads
                {
                    HttpContext.Current = _fakeContext;   // FB-402: pool threads have no HttpContext either

                    var fee = BillingRunService.CalculateAccountFee(acct.Id, run.PeriodEnd);
                    _db.Invoices.Add(new Invoice { AccountId = acct.Id, Amount = fee, RunId = run.Id });
                    _reportingDb.FeeFacts.Add(new FeeFact
                    {
                        RunId = run.Id,
                        FirmId = run.FirmId,
                        AccountId = acct.Id,
                        PeriodEnd = run.PeriodEnd,
                        ScheduleCode = acct.FeeSchedule.Code,
                        FeeAmount = fee,
                        LoadedOn = DateTime.Now
                    });
                });

                if (ConfigurationManager.AppSettings["Feature.HouseholdBilling"] == "true")
                {
                    var householdIds = accounts
                        .Where(BillingRunService.IsHouseholdBilled)
                        .Select(a => a.HouseholdId.Value)
                        .Distinct()
                        .ToList();

                    foreach (var householdId in householdIds)
                    {
                        var allocations = HouseholdFeeService.CalculateAndAllocate(householdId, run.PeriodEnd);
                        foreach (var allocation in allocations)
                        {
                            _db.Invoices.Add(new Invoice { AccountId = allocation.Key, HouseholdId = householdId, Amount = allocation.Value, RunId = run.Id });
                            _reportingDb.FeeFacts.Add(new FeeFact
                            {
                                RunId = run.Id,
                                FirmId = run.FirmId,
                                AccountId = allocation.Key,
                                HouseholdId = householdId,
                                PeriodEnd = run.PeriodEnd,
                                FeeAmount = allocation.Value,
                                LoadedOn = DateTime.Now
                            });
                        }
                    }
                }

                _db.SaveChanges();
                _reportingDb.SaveChanges();
                scope.Complete();
            }
            run.Status = "Complete";   // crash at account 40,000 = whole run rolls back, starts over
            run.CompletedOn = DateTime.Now;
            _db.SaveChanges();

            Log.Info("Billing run " + run.Id + " complete");
        }
    }
}
