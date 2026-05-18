using System.Threading;
using System.Threading.Tasks;
using Core.DTOs;

namespace Core.Interfaces
{
    public interface IFloodForecastService
    {
        Task<FloodForecastResponseDto?> RunForecastForCitizenAsync(
            double latitude,
            double longitude,
            double radiusKm = 3.0,
            int? dataDaysBack = null,
            CancellationToken cancellationToken = default);
    }
}
