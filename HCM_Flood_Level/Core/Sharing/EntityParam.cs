using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Sharing
{
    public class EntityParam
    {
        //Sorting
        public string Sorting { get; set; }

        //Filter
        public int? RoleId { get; set; }
        public int? SensorId { get; set; }
        public string? ScheduleStatus { get; set; }
        public string? ScheduleType { get; set; }
        public string? ScheduleMode { get; set; }
        public string? RequestStatus { get; set; }
        public string RequestPriority { get; set; }

        //Page size
        public int maxpagesize { get; set; } = 100;
        private int pagesize = 10;
        public int Pagesize
        {
            get => pagesize;
            set => pagesize = value > maxpagesize ? maxpagesize : value;
        }
        public int Pagenumber { get; set; } = 1;

        //Search
        private string _search;

        public string Search
        {
            get => _search;
            set => _search = value?.ToLower();
        }
    }

    public enum StaffDeleteUserResult
    {
        Success,
        UserNotFound,
        TargetNotTechnician,
        TechnicianHasIncompleteWork
    }
}
