using LettriisMaui.Resources.Themes;
using Microsoft.Maui.Storage;

namespace LettriisMaui.Services;

public sealed class ThemeService
{
    // Saved manual theme choice (when ThemeAuto == false, or as the fallback when Auto is true)
    private const string ThemeKey = "theme_name";

    // Whether we should automatically select seasonal themes by date
    private const string ThemeAutoKey = "ThemeAuto";

    public string CurrentThemeName
    {
        get => Preferences.Get(ThemeKey, "Default");
        set => Preferences.Set(ThemeKey, value);
    }

    public bool ThemeAuto
    {
        get => Preferences.Get(ThemeAutoKey, true);
        set => Preferences.Set(ThemeAutoKey, value);
    }

    /// <summary>
    /// Applies the currently effective theme (manual or auto-seasonal),
    /// without overwriting the user's manual selection when Auto is enabled.
    /// Call this from App ctor/startup before UI loads.
    /// </summary>
    public void ApplySavedTheme()
    {
        var effective = GetEffectiveThemeName();
        ApplyTheme(effective, persistSelection: !ThemeAuto);
    }

    /// <summary>
    /// Applies the specified theme dictionary to Application resources.
    /// If persistSelection is true, saves it as the user's manual theme choice.
    /// </summary>
    public void ApplyTheme(string themeName, bool persistSelection = true)
    {
        var app = Application.Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;

        // Remove any previously applied theme dictionaries (identified by ThemeName key)
        var toRemove = merged.Where(d => d.ContainsKey("ThemeName")).ToList();
        foreach (var d in toRemove)
            merged.Remove(d);

        merged.Add(CreateThemeDictionarySafe(themeName));

        if (persistSelection)
            CurrentThemeName = themeName;
    }

    /// <summary>
    /// Returns the theme that should be used right now.
    /// If Auto is enabled and we're in a seasonal window, returns that theme name.
    /// Otherwise returns the user's saved theme.
    /// </summary>
    public string GetEffectiveThemeName()
    {
        var savedTheme = Preferences.Get(ThemeKey, "Default");

        if (!ThemeAuto)
            return savedTheme;

        var today = DateTime.Now; // local time

        if (IsChristmasWindow(today))
            return "Christmas";

        return savedTheme;
    }

    // -----------------------------
    // Seasonal windows
    // -----------------------------

    public static bool IsInWindow(DateTime todayLocal, int startMonth, int startDay, int endMonth, int endDay)
    {
        var y = todayLocal.Year;
        var start = new DateTime(y, startMonth, startDay);
        var end = new DateTime(y, endMonth, endDay);

        // If end < start, window crosses New Year (e.g., Dec 15 -> Jan 5)
        if (end < start)
        {
            // If today is before end, treat start as last year
            if (todayLocal < end) start = start.AddYears(-1);
            else end = end.AddYears(1);
        }

        return todayLocal >= start && todayLocal <= end;
    }

    // Dec 15 -> Jan 25 : Christmas theme window
    public static bool IsChristmasWindow(DateTime todayLocal)
        => IsInWindow(todayLocal, 12, 15, 1, 5);

    // -----------------------------
    // Theme dictionary creation
    // -----------------------------

    private static ResourceDictionary CreateThemeDictionarySafe(string themeName)
    {
        try
        {
            return CreateThemeDictionary(themeName);
        }
        catch
        {
            // Never let theme loading crash the game
            return new Theme_Default();
        }
    }

    private static ResourceDictionary CreateThemeDictionary(string themeName) =>
        themeName switch
        {
            "Default" => new Theme_Default(),
            "Alt" => new Theme_Alt(),
            "Christmas" => new Theme_Christmas(),
            _ => new Theme_Default()
        };
}
