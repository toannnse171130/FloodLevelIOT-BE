using Core.Entities;
using Core.Sharing;

namespace Core.Interfaces
{
    public interface IHistoryRepository
    {
        Task<IReadOnlyList<History>> GetAllAsync(EntityParam param);
        Task<IReadOnlyList<History>> GetFilteredAsync(int? placeId, int hours, int limit);
        Task<int> CountAsync();
        Task<History> AddAsync(History history);
    }
}
