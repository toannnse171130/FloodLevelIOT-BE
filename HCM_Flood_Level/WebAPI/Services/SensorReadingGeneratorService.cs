using Core.Entities;
using Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WebAPI.Services
{
    public class SensorReadingGeneratorService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);
        private readonly int _maxReadingsPerSensor = 200;
        private readonly Random _rnd = new Random();

        public SensorReadingGeneratorService(IServiceProvider services)
        {
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // run immediately once, then wait interval
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await GenerateOnce(stoppingToken);
                }
                catch (Exception)
                {
                    // Error while generating sensor readings
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task GenerateOnce(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var sensorIds = (await uow.ManageSensorRepository.GetAllSensorIdsAsync()).ToList();
            if (!sensorIds.Any()) return;

            // Exclude real hardware sensors from mock data generation
            var realSensorCodes = new HashSet<string> { "ESP32_01" };

            foreach (var sensorId in sensorIds)
            {
                if (ct.IsCancellationRequested) break;

                var sensorCheck = await uow.ManageSensorRepository.GetByIdAsync(sensorId);
                if (sensorCheck != null && !string.IsNullOrEmpty(sensorCheck.SensorCode) && realSensorCodes.Contains(sensorCheck.SensorCode))
                    continue;

                // Generate values
                var status = _rnd.NextDouble() > 0.05 ? "Online" : "Offline"; // mostly online
                var signalRoll = _rnd.NextDouble();
                var signal = signalRoll > 0.8 ? "Mất kết nối" : (signalRoll > 0.3 ? "Ổn định" : "Yếu");

                // For water level, try to use sensor's MaxLevel if available; otherwise max 200
                int maxLevel = 200;
                // fetch sensor to read MaxLevel (lightweight)
                var sensor = await uow.ManageSensorRepository.GetByIdAsync(sensorId);
                if (sensor != null && sensor.MaxLevel > 0)
                    maxLevel = (int)sensor.MaxLevel;

                // Compute battery so that it decays linearly over ~30 days from 100% to 0%.
                // Use InstalledAt if available, otherwise CreatedAt.
                DateTime startTime = sensor?.InstalledAt ?? sensor?.CreatedAt ?? DateTime.UtcNow;
                var elapsedDays = (DateTime.UtcNow - startTime).TotalDays;
                var proportion = elapsedDays / 30.0; // 1.0 => depleted
                var baseBattery = (int)Math.Round(Math.Max(0.0, 100.0 * (1.0 - proportion)));
                // Add small random noise so readings vary slightly but overall trend is linear
                var noise = _rnd.Next(-5, 3); // -5..2
                var battery = Math.Clamp(baseBattery + noise, 0, 100);

                var waterLevel = Math.Round(_rnd.NextDouble() * maxLevel, 2);

                var reading = new SensorReading
                {
                    SensorId = sensorId,
                    Status = status,
                    WaterLevelCm = (float)waterLevel,
                    BatteryPercent = (int)battery,
                    SignalStrength = signal,
                    RecordedAt = DateTime.UtcNow
                };

                await uow.ManageSensorRepository.AddSensorReadingAsync(reading);

                // prune to max entries
                await uow.ManageSensorRepository.PruneSensorReadingsAsync(sensorId, _maxReadingsPerSensor);

                // check history (flood) highest level
                var existingMax = await uow.ManageSensorRepository.GetMaxHistoryLevelForSensorAsync(sensorId);
                if (!existingMax.HasValue || reading.WaterLevelCm > existingMax.Value)
                {
                    var history = new History
                    {
                        LocationId = sensor?.PlaceId ?? 0,
                        StartTime = reading.RecordedAt,
                        MaxWaterLevel = reading.WaterLevelCm,
                        Severity = Severity.Warning, // Default to Warning for new max levels
                        CreatedAt = DateTime.UtcNow
                    };

                    await uow.ManageSensorRepository.AddHistoryAsync(history);
                }
            }
        }
    }
}
