namespace LettriisMaui.Models;

public sealed class GameStartOptions
{
    public string Username { get; set; } = "";

    public int StartingLevel { get; set; } = 1;

    // Preset that maps to internal tuning
    public DifficultyPreset Difficulty { get; set; } = DifficultyPreset.Standard;

    // Theme key/name (maps to ThemeService resource set)
    public string Theme { get; set; } = "Default";
}

public enum DifficultyPreset
{
    Casual,
    Standard,
    Hard,
    Insane
}