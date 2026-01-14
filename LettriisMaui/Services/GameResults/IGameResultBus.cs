using LettriisMaui.Models;

namespace LettriisMaui.Services.GameResults;

public interface IGameResultBus
{
    void Set(GameResult result);
    GameResult? GetAndClear();
}