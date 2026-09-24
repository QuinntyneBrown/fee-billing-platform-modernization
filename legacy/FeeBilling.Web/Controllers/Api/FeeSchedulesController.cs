using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Http;
using FeeBilling.Data;
using FeeBilling.Web.Models;
using log4net;

namespace FeeBilling.Web.Controllers.Api
{
    [RoutePrefix("api/feeschedules")]
    public class FeeSchedulesController : ApiController
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(FeeSchedulesController));

        private readonly FeeBillingEntities _db;

        // Injected by Unity (UnityConfig). Not the same context as DbContextFactory.Current.
        public FeeSchedulesController(FeeBillingEntities db)
        {
            _db = db;
        }

        [HttpGet, Route("")]
        public IEnumerable<FeeScheduleDto> GetSchedules()
        {
            return _db.FeeSchedules
                .Include(s => s.Tiers)
                .OrderBy(s => s.Code)
                .ToList()
                .Select(FeeScheduleDto.From);
        }

        [HttpGet, Route("{id:int}", Name = "GetFeeSchedule")]
        public IHttpActionResult GetSchedule(int id)
        {
            var schedule = _db.FeeSchedules.Include(s => s.Tiers).SingleOrDefault(s => s.Id == id);
            if (schedule == null) return NotFound();
            return Ok(FeeScheduleDto.From(schedule));
        }

        [HttpPost, Route("")]
        [Authorize(Roles = "ScheduleEditor")]
        public IHttpActionResult CreateSchedule(FeeScheduleDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            if (_db.FeeSchedules.Any(s => s.Code == dto.Code))
            {
                ModelState.AddModelError("Code", "A schedule with code " + dto.Code + " already exists.");
                return BadRequest(ModelState);
            }

            var schedule = new FeeSchedule
            {
                CreatedOn = DateTime.Now,
                IsActive = true
            };
            dto.ApplyTo(schedule, _db);
            _db.FeeSchedules.Add(schedule);
            _db.SaveChanges();

            // No audit trail beyond ModifiedBy (FB-340).
            Log.Info("Fee schedule " + schedule.Code + " created by " + HttpContext.Current.User.Identity.Name);
            return CreatedAtRoute("GetFeeSchedule", new { id = schedule.Id }, FeeScheduleDto.From(schedule));
        }

        [HttpPut, Route("{id:int}")]
        [Authorize(Roles = "ScheduleEditor")]
        public IHttpActionResult UpdateSchedule(int id, FeeScheduleDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var schedule = _db.FeeSchedules.Include(s => s.Tiers).SingleOrDefault(s => s.Id == id);
            if (schedule == null) return NotFound();

            // Changing a schedule mid-quarter changes every open invoice that uses it.
            // Nothing stops that.
            schedule.ModifiedOn = DateTime.Now;
            schedule.ModifiedBy = HttpContext.Current.User.Identity.Name;
            dto.ApplyTo(schedule, _db);
            _db.SaveChanges();

            Log.Info("Fee schedule " + schedule.Code + " updated by " + schedule.ModifiedBy);
            return Ok(FeeScheduleDto.From(schedule));
        }
    }
}
