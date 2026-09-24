using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using FeeBilling.Data;
using FeeBilling.Web.Models;

namespace FeeBilling.Web.Controllers.Api
{
    // MIGRATED: /api/accounts is now served by FeeBilling.Accounts.Api via the YARP gateway.
    // Kept for rollback. Do not add features here.
    [RoutePrefix("api/accounts")]
    public class AccountsController : ApiController
    {
        [HttpGet, Route("")]
        public IEnumerable<AccountDto> GetAccounts(int firmId, string search = null)
        {
            var db = DbContextFactory.Current;
            var query = db.Accounts.Where(a => a.FirmId == firmId);
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(a => a.AccountNumber.Contains(search) || a.AccountName.Contains(search));
            }

            return query
                .OrderBy(a => a.AccountName)
                .ToList()
                .Select(AccountDto.From);   // Household / FeeSchedule lazy-loaded per account
        }

        [HttpGet, Route("{id:int}")]
        public IHttpActionResult GetAccount(int id)
        {
            var db = DbContextFactory.Current;
            var account = db.Accounts.Find(id);
            if (account == null) return NotFound();
            return Ok(AccountDto.From(account));
        }

        [HttpGet, Route("{id:int}/positions")]
        public IHttpActionResult GetPositions(int id, DateTime asOf)
        {
            var db = DbContextFactory.Current;
            var account = db.Accounts.Find(id);
            if (account == null) return NotFound();

            var positions = account.Positions        // lazy: loads EVERY position for the account, then filters in memory
                .Where(p => p.AsOfDate == asOf.Date)
                .OrderBy(p => p.SecurityCode)
                .Select(p => new { p.SecurityCode, p.SecurityName, p.Quantity, p.MarketValue, p.IsCashSleeve });
            return Ok(positions);
        }
    }
}
