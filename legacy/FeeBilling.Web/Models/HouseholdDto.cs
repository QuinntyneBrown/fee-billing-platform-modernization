using System.Collections.Generic;
using System.Linq;
using FeeBilling.Data;

namespace FeeBilling.Web.Models
{
    public class HouseholdDto
    {
        public int Id { get; set; }
        public int FirmId { get; set; }
        public string HouseholdCode { get; set; }
        public string Name { get; set; }
        public int? FeeScheduleId { get; set; }
        public string FeeScheduleCode { get; set; }
        public int MemberCount { get; set; }
        public List<AccountDto> Members { get; set; }

        public static HouseholdDto From(Household h, bool includeMembers)
        {
            return new HouseholdDto
            {
                Id = h.Id,
                FirmId = h.FirmId,
                HouseholdCode = h.HouseholdCode,
                Name = h.Name,
                FeeScheduleId = h.FeeScheduleId,
                FeeScheduleCode = h.FeeSchedule != null ? h.FeeSchedule.Code : null,
                MemberCount = h.Accounts.Count,
                Members = includeMembers ? h.Accounts.OrderBy(a => a.Id).Select(AccountDto.From).ToList() : null
            };
        }
    }
}
