namespace LettriisMaui.Models;

public sealed class GameResult
{
    public int Score { get; set; }
    public int Level { get; set; }
    public int Lines { get; set; }
    public int WordsCleared { get; set; }
    public TimeSpan Duration { get; set; }

    public DateTimeOffset EndedAt { get; set; } = DateTimeOffset.UtcNow;

    // Optional: include these if you already track them
    public string ModeKey { get; set; } = "classic";
    public string OptionsHash { get; set; } = "";
}