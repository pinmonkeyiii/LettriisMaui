namespace LettriisMaui.Models;

public sealed class HighScoreEntry
{
    public string PlayerName { get; set; } = "";
    public int Score { get; set; }
    public int Level { get; set; }
    public int Lines { get; set; }
    public int WordsCleared { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTimeOffset AchievedAt { get; set; } = DateTimeOffset.UtcNow;

    // Optional but recommended for future filtering:
    public string ModeKey { get; set; } = "classic";
    public string OptionsHash { get; set; } = "";
}