using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.DTOs
{
    public class LocationDTO
    {
        public int PlaceId { get; set; }
        public int AreaId { get; set; }
        public string AreaName { get; set; }
        public string? Title { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Address { get; set; }
    }

    public class LocationAreaDTO
    {
        public int PlaceId { get; set; }
        public string? Title { get; set; }
        public string? Address { get; set; }
    }
}
