using AutoMapper;
using Core.DTOs;
using Core.Entities;

namespace WebAPI.Models
{
    public class MappingLocation : Profile
    {
        public MappingLocation()
        {
            CreateMap<Location, LocationDTO>()
                .ForMember(a => a.PlaceId, a => a.MapFrom(b => b.PlaceId))
                .ForMember(a => a.AreaId, a => a.MapFrom(b => b.AreaId))
                .ForMember(a => a.AreaName, a => a.MapFrom(b => b.Area != null ? b.Area.AreaName : null))
                .ForMember(a => a.Title, a => a.MapFrom(b => b.Title))
                .ForMember(a => a.Address, a => a.MapFrom(b => b.Address))
                .ForMember(a => a.Latitude, a => a.MapFrom(b => b.Latitude))
                .ForMember(a => a.Longitude, a => a.MapFrom(b => b.Longitude));
        }
    }
}
