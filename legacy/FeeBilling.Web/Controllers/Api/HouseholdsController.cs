using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using FeeBilling.Data;
using FeeBilling.Web.Models;

namespace FeeBilling.Web.Controllers.Api
{
    // MIGRATED: /api/households is now served by FeeBilling.Accounts.Api via the YARP gateway.
    // Kept for rollback. Do not add features here.
    [RoutePrefix("api/households")]
    public class HouseholdsController : ApiController
    {
        [HttpGet, Route("")]
        public IEnumerable<HouseholdDto> GetHouseholds(int firmId)
        {
            var db = DbContextFactory.Current;
            return db.Households
                .Where(h => h.FirmId == firmId)
                .OrderBy(h => h.HouseholdCode)
                .ToList()
                .Select(h => HouseholdDto.From(h, includeMembers: false));
        }

        [HttpGet, Route("{id:int}")]
        public IHttpActionResult GetHousehold(int id)
        {
            var db = DbContextFactory.Current;
            var household = db.Households.Find(id);
            if (household == null) return NotFound();
            return Ok(HouseholdDto.From(household, includeMembers: true));
        }
    }
}
