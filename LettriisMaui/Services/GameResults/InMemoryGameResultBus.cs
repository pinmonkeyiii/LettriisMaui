using LettriisMaui.Models;

namespace LettriisMaui.Services.GameResults;

public sealed class InMemoryGameResultBus : IGameResultBus
{
    private readonly object _lock = new();
    private GameResult? _last;

    public void Set(GameResult result)
    {
        lock (_lock)
            _last = result;
    }

    public GameResult? GetAndClear()
    {
        lock (_lock)
        {
            var r = _last;
            _last = null;
            return r;
        }
    }
}