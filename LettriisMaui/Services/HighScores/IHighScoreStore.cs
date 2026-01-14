using LettriisMaui.Models;

namespace LettriisMaui.Services.HighScores;

public interface IHighScoreStore
{
    Task<IReadOnlyList<HighScoreEntry>> GetAsync(CancellationToken ct = default);
    Task SubmitAsync(HighScoreEntry entry, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}