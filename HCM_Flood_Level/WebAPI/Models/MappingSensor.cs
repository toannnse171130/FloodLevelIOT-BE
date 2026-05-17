using AutoMapper;
using Core.DTOs;
using Core.Entities;
using System;

namespace WebAPI.Models
{
    public class MappingSensor : Profile
    {
        public MappingSensor()
        {
            CreateMap<Sensor, ManageSensorDTO>()
                .ForMember(a => a.SensorId, a => a.MapFrom(b => b.SensorId))
                .ForMember(a => a.SensorName, a => a.MapFrom(b => b.SensorName))
                .ForMember(a => a.Title, a => a.MapFrom(b => b.Location != null ? b.Location.Title : null))
                .ForMember(a => a.Latitude, a => a.MapFrom(b => b.Location != null ? (double)b.Location.Latitude : 0))
                .ForMember(a => a.Longitude, a => a.MapFrom(b => b.Location != null ? (double)b.Location.Longitude : 0))
                .ForMember(a => a.InstalledAt, a => a.MapFrom(b => b.InstalledAt))
                .ForMember(d => d.Status, opt => opt.MapFrom((src, dest, destMember, ctx) =>
                {
                    if (ctx.Items.TryGetValue("LatestReadings", out var obj) && obj is System.Collections.Generic.Dictionary<int, SensorReading> dict && dict.TryGetValue(src.SensorId, out var rd))
                        return rd?.Status;
                    return null;
                }))
                .ForMember(d => d.WaterLevel, opt => opt.MapFrom((src, dest, destMember, ctx) =>
                {
                    if (ctx.Items.TryGetValue("LatestReadings", out var obj) && obj is System.Collections.Generic.Dictionary<int, SensorReading> dict && dict.TryGetValue(src.SensorId, out var rd))
                        return rd?.WaterLevelCm;
                    return null;
                }))
                .ForMember(d => d.SignalStrength, opt => opt.MapFrom((src, dest, destMember, ctx) =>
                {
                    if (ctx.Items.TryGetValue("LatestReadings", out var obj) && obj is System.Collections.Generic.Dictionary<int, SensorReading> dict && dict.TryGetValue(src.SensorId, out var rd))
                        return rd?.SignalStrength;
                    return null;
                }));

            CreateMap<Sensor, SensorDTO>()
                .ForMember(a => a.SensorId, a => a.MapFrom(b => b.SensorId))
                .ForMember(a => a.SensorCode, a => a.MapFrom(b => b.SensorCode))
                .ForMember(a => a.Protocol, a => a.MapFrom(b => b.Protocol))
                .ForMember(a => a.WarrantyDate, a => a.MapFrom(b => b.CreatedAt.AddMonths(12)))
                .ForMember(a => a.SensorType, a => a.MapFrom(b => b.SensorType))
                .ForMember(a => a.WarningThreshold, a => a.MapFrom(b => b.WarningThreshold ?? 0))
                .ForMember(a => a.DangerThreshold, a => a.MapFrom(b => b.DangerThreshold ?? 0))
                .ForMember(a => a.MaxLevel, a => a.MapFrom(b => b.MaxLevel))
                .ForMember(d => d.Battery, opt => opt.MapFrom((src, dest, destMember, ctx) =>
                {
                    if (ctx.Items.TryGetValue("LatestReadings", out var obj) && obj is System.Collections.Generic.Dictionary<int, SensorReading> dict && dict.TryGetValue(src.SensorId, out var rd))
                        return rd?.BatteryPercent;
                    return null;
                }))
                .ForMember(a => a.InstalledAt, a => a.MapFrom(b => b.InstalledAt ?? b.CreatedAt))
                .ForMember(a => a.CommissionedAt, a => a.MapFrom(b => b.CreatedAt))
                .ForMember(a => a.TechnicianId, a => a.MapFrom(b => b.TechnicianId))
                .ForMember(a => a.InstalledByStaff, a => a.MapFrom(b => b.Technician != null ? b.Technician.FullName : string.Empty))
                .ForMember(a => a.Location, a => a.MapFrom(b => b.Location))
                .ForMember(d => d.WaterLevel, opt => opt.MapFrom((src, dest, destMember, ctx) =>
                {
                    if (ctx.Items.TryGetValue("LatestReadings", out var obj) && obj is System.Collections.Generic.Dictionary<int, SensorReading> dict && dict.TryGetValue(src.SensorId, out var rd))
                        return rd?.WaterLevelCm;
                    return null;
                }))
                .ForMember(d => d.Status, opt => opt.MapFrom((src, dest, destMember, ctx) =>
                {
                    if (ctx.Items.TryGetValue("LatestReadings", out var obj) && obj is System.Collections.Generic.Dictionary<int, SensorReading> dict && dict.TryGetValue(src.SensorId, out var rd))
                        return rd?.Status;
                    return null;
                }))
                .ForMember(d => d.RecordAt, opt => opt.MapFrom((src, dest, destMember, ctx) =>
                {
                    if (ctx.Items.TryGetValue("LatestReadings", out var obj) && obj is System.Collections.Generic.Dictionary<int, SensorReading> dict && dict.TryGetValue(src.SensorId, out var rd))
                        return rd?.RecordedAt;
                    return null;
                }));

            CreateMap<CreateSensorDTO, Sensor>()
                .ForMember(a => a.PlaceId, a => a.Ignore())
                .ForMember(a => a.TechnicianId, a => a.MapFrom(b => b.TechnicianId))
                .ForMember(a => a.Specification, a => a.MapFrom(b => b.Specification))
                .ForMember(a => a.SensorCode, a => a.MapFrom(b => b.SensorCode))
                .ForMember(a => a.SensorName, a => a.MapFrom(b => b.SensorName))
                .ForMember(a => a.Protocol, a => a.MapFrom(b => b.Protocol))
                .ForMember(a => a.SensorType, a => a.MapFrom(b => b.SensorType))
                .ForMember(a => a.WarningThreshold, a => a.MapFrom(b => b.WarningThreshold))
                .ForMember(a => a.DangerThreshold, a => a.MapFrom(b => b.DangerThreshold))
                .ForMember(a => a.MaxLevel, a => a.MapFrom(b => b.MaxLevel));

            CreateMap<UpdateSensorDTO, Sensor>()
                .ForMember(a => a.SensorId, a => a.Ignore())
                .ForMember(a => a.InstalledAt, a => a.Ignore())
                .ForMember(a => a.Location, a => a.Ignore())
                .ForMember(a => a.SensorCode, a => a.MapFrom(b => b.SensorCode))
                .ForMember(a => a.SensorName, a => a.MapFrom(b => b.SensorName))
                .ForMember(a => a.Protocol, a => a.MapFrom(b => b.Protocol))
                .ForMember(a => a.SensorType, a => a.MapFrom(b => b.SensorType))
                .ForMember(a => a.TechnicianId, a => a.MapFrom(b => b.TechnicianId))
                .ForMember(a => a.Specification, a => a.MapFrom(b => b.Specification))
                .ForMember(a => a.WarningThreshold, a => a.MapFrom(b => b.WarningThreshold))
                .ForMember(a => a.DangerThreshold, a => a.MapFrom(b => b.DangerThreshold))
                .ForMember(a => a.MaxLevel, a => a.MapFrom(b => b.MaxLevel))
                .ForMember(a => a.PlaceId, a => a.MapFrom(b => b.PlaceId))
                .ForAllMembers(opt => opt.Condition((src, dest, srcMember) => srcMember != null));
        }
    }
}
