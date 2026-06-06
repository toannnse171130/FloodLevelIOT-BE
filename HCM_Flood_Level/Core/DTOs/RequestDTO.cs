using Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.DTOs
{
    public class RequestDTO
    {
        public int RequestId { get; set; }
        public string SensorName { get; set; }
        public string Priority { get; set; }
        public string? Description { get; set; }
        public DateTime? Deadline { get; set; }
        public string? AssignedTechnicianTo { get; set; }
        public string? Note { get; set; }
        public string Status { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class StaffCreateRequestDTO
    {
        public int SensorId { get; set; }
        public int Priorityid { get; set; }
        public int AssignedTechnicianTo { get; set; }
        public string? Description { get; set; }
        public DateTime? Deadline { get; set; }
        public string? Note { get; set; }
    }

    public class TechnicianUpdateStatusDTO
    {
        public string Status { get; set; }
    }

    // Staff edits an existing maintenance request (partial update). Status is managed by technician separately.
    public class StaffUpdateRequestDTO
    {
        public int? Priorityid { get; set; }
        public int? AssignedTechnicianTo { get; set; }
        public string? Description { get; set; }
        public DateTime? Deadline { get; set; }
        public string? Note { get; set; }
    }
}
