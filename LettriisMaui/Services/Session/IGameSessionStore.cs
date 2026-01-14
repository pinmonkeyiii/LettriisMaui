using System.Threading.Tasks;
using LettriisMaui.Models.Session;

namespace LettriisMaui.Services.Session
{
    public interface IGameSessionStore
    {
        Task SaveAsync(GameSessionDto dto, CancellationToken ct = default);
        Task<GameSessionDto?> LoadAsync(CancellationToken ct = default);
        Task ClearAsync(CancellationToken ct = default);
        Task<bool> HasSessionAsync();
    }
}