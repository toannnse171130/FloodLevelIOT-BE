using Core.Entities;
using Core.Sharing;

namespace Core.Interfaces
{
    public interface IHistoryRepository
    {
        Task<IReadOnlyList<History>> GetAllAsync(EntityParam param);
        Task<int> CountAsync();
    }
}
