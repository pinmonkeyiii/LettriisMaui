using LettriisMaui.Models;
using Microsoft.Maui.Storage;

namespace LettriisMaui.Services;

public interface IGameStartOptionsService
{
    GameStartOptions Current { get; }

    void LoadFromPreferences();
    void SaveToPreferences();

    void SetUsername(string username);
    void SetStartingLevel(int level);
    void SetDifficulty(DifficultyPreset preset);
    void SetTheme(string theme);

    void ResetToDefaults();
}

public sealed class GameStartOptionsService : IGameStartOptionsService
{
    private const string KUsername = "username";
    private const string KStartLevel = "start_level";
    private const string KDifficulty = "difficulty";
    private const string KTheme = "theme";

    public GameStartOptions Current { get; } = new();

    public void LoadFromPreferences()
    {
        Current.Username = (Preferences.Get(KUsername, "") ?? "").Trim();

        var level = Preferences.Get(KStartLevel, 1);
        Current.StartingLevel = Math.Clamp(level, 1, 20);

        var diffRaw = Preferences.Get(KDifficulty, DifficultyPreset.Standard.ToString());
        if (!Enum.TryParse(diffRaw, ignoreCase: true, out DifficultyPreset preset))
            preset = DifficultyPreset.Standard;
        Current.Difficulty = preset;

        Current.Theme = Preferences.Get(KTheme, "Default") ?? "Default";
    }

    public void SaveToPreferences()
    {
        Preferences.Set(KUsername, Current.Username);
        Preferences.Set(KStartLevel, Current.StartingLevel);
        Preferences.Set(KDifficulty, Current.Difficulty.ToString());
        Preferences.Set(KTheme, Current.Theme);
    }

    public void SetUsername(string username) => Current.Username = (username ?? "").Trim();

    public void SetStartingLevel(int level) => Current.StartingLevel = Math.Clamp(level, 1, 20);

    public void SetDifficulty(DifficultyPreset preset) => Current.Difficulty = preset;

    public void SetTheme(string theme) => Current.Theme = string.IsNullOrWhiteSpace(theme) ? "Default" : theme.Trim();

    public void ResetToDefaults()
    {
        Current.Username = "";
        Current.StartingLevel = 1;
        Current.Difficulty = DifficultyPreset.Standard;
        Current.Theme = "Default";
    }
}