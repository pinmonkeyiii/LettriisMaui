using Microsoft.Maui.Storage;

namespace LettriisMaui.Services;

public sealed class SettingsService
{
    private const string BestScoreKey = "best_score";
    private const string SfxEnabledKey = "sfx_enabled";
    private const string MusicEnabledKey = "music_enabled";

    public string Username
    {
        get => Preferences.Get("username", "");
        set => Preferences.Set("username", value?.Trim() ?? "");
    }

    public int BestScore
    {
        get => Preferences.Get(BestScoreKey, 0);
        set => Preferences.Set(BestScoreKey, Math.Max(0, value));
    }

    public bool SfxEnabled
    {
        get => Preferences.Get(SfxEnabledKey, true);
        set => Preferences.Set(SfxEnabledKey, value);
    }

    public bool MusicEnabled
    {
        get => Preferences.Get(MusicEnabledKey, true);
        set => Preferences.Set(MusicEnabledKey, value);
    }
}