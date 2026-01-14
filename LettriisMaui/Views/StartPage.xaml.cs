using LettriisMaui.Models;
using LettriisMaui.Services;
using LettriisMaui.Services.HighScores;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;

namespace LettriisMaui.Views;

public partial class StartPage : ContentPage
{
    private readonly IGameStartOptionsService _options;
    private readonly IHighScoreStore _highScoreStore;

    private int _startLevel = 1;

    // UI list (rank/name/score) - now backed by persisted store
    private readonly ObservableCollection<HighScoreRow> _scores = new();

    public StartPage(IGameStartOptionsService options, IHighScoreStore highScoreStore)
    {
        InitializeComponent();
        _options = options;
        _highScoreStore = highScoreStore;

        // Picker sources
        DifficultyPicker.ItemsSource = Enum.GetNames(typeof(DifficultyPreset)).ToList();
        ThemePicker.ItemsSource = new List<string> { "Default", "Neon", "Classic", "Midnight" };

        HighScoresList.ItemsSource = _scores;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Layout pass
        ApplyResponsiveLayout(Width);

        // Load saved options (username, difficulty, level, theme)
        _options.LoadFromPreferences();

        UserEntry.Text = _options.Current.Username;

        _startLevel = _options.Current.StartingLevel;
        LevelLabel.Text = _startLevel.ToString();

        DifficultyPicker.SelectedItem = _options.Current.Difficulty.ToString();
        ThemePicker.SelectedItem = _options.Current.Theme;

        // Seed Play button enabled state
        UpdatePlayEnabled();

        // Load persisted scores
        await LoadHighScoresAsync();

        // Desktop focus
        if (DeviceInfo.Idiom != DeviceIdiom.Phone)
            Dispatcher.Dispatch(() => UserEntry.Focus());
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        ApplyResponsiveLayout(width);
        ClampContentWidth(width);
        FitPanelsToViewport(height);
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (width <= 0) return;

        if (width < 820)
        {
            MainGrid.ColumnDefinitions.Clear();
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            MainGrid.RowDefinitions.Clear();
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            LeftPanel.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 0);
            LeftPanel.SetValue(Microsoft.Maui.Controls.Grid.RowProperty, 0);

            RightPanel.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 0);
            RightPanel.SetValue(Microsoft.Maui.Controls.Grid.RowProperty, 1);
        }
        else
        {
            MainGrid.RowDefinitions.Clear();
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            MainGrid.ColumnDefinitions.Clear();
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

            LeftPanel.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 0);
            LeftPanel.SetValue(Microsoft.Maui.Controls.Grid.RowProperty, 0);

            RightPanel.SetValue(Microsoft.Maui.Controls.Grid.ColumnProperty, 1);
            RightPanel.SetValue(Microsoft.Maui.Controls.Grid.RowProperty, 0);
        }
    }

    private void ClampContentWidth(double pageWidth)
    {
        if (pageWidth <= 0) return;

        // Your ContentHost already has Padding="24" so reserve that on both sides
        const double outerPadding = 24 * 2;

        // On Windows, reserve a little extra so content never sits under the overlay scrollbar
        var scrollbarGutter = DeviceInfo.Platform == DevicePlatform.WinUI ? 28 : 0;

        var target = Math.Min(1120, pageWidth - outerPadding - scrollbarGutter);

        // Avoid negative/too-small values
        ContentHost.WidthRequest = Math.Max(320, target);
    }

    private void FitPanelsToViewport(double pageHeight)
    {
        if (pageHeight <= 0) return;

        // If options aren’t open, no special work needed.
        if (!OptionsPanel.IsVisible)
        {
            OptionsScroll.MaximumHeightRequest = 420; // default
            return;
        }

        // Rough vertical budget:
        // page height minus ContentHost top/bottom margins and padding.
        // (Your ContentHost has Margin="0,40,0,40" and Padding="24")
        const double outerMargin = 40 * 2;
        const double outerPadding = 24 * 2;

        // Header area (Lettriis title + subtitle + spacing)
        const double headerApprox = 110;

        // Main card padding + stroke breathing room
        const double cardChrome = 18 * 2 + 20;

        // Space already used by Sign In panel content above Options
        const double signInApprox = 230;

        // Leave a little breathing room so it never kisses the bottom edge
        const double safety = 24;

        var availableForOptions =
            pageHeight - outerMargin - outerPadding - headerApprox - cardChrome - signInApprox - safety;

        // Clamp to something reasonable; if the window is short, Options will scroll internally.
        OptionsScroll.MaximumHeightRequest = Math.Max(160, Math.Min(520, availableForOptions));
    }

    // --- Username / Enter-to-play ---
    private void OnUserEntryCompleted(object sender, EventArgs e)
        => OnPlay(sender, e);

    private void OnUsernameChanged(object sender, TextChangedEventArgs e)
        => UpdatePlayEnabled();

    private void UpdatePlayEnabled()
    {
        var name = (UserEntry.Text ?? "").Trim();
        PlayButton.IsEnabled = !string.IsNullOrWhiteSpace(name);
    }

    // --- Options panel toggle ---
    private void OnToggleOptions(object sender, EventArgs e)
    {
        OptionsPanel.IsVisible = !OptionsPanel.IsVisible;
        OptionsButton.Text = OptionsPanel.IsVisible ? "Close" : "Options";

        FitPanelsToViewport(Height);
    }

    private void OnLevelMinus(object sender, EventArgs e)
    {
        _startLevel = Math.Max(1, _startLevel - 1);
        LevelLabel.Text = _startLevel.ToString();
    }

    private void OnLevelPlus(object sender, EventArgs e)
    {
        _startLevel = Math.Min(20, _startLevel + 1);
        LevelLabel.Text = _startLevel.ToString();
    }

    private void OnResetOptions(object sender, EventArgs e)
    {
        _startLevel = 1;
        LevelLabel.Text = "1";

        DifficultyPicker.SelectedItem = DifficultyPreset.Standard.ToString();
        ThemePicker.SelectedItem = "Default";
    }

    private void OnApplyOptions(object sender, EventArgs e)
    {
        var name = (UserEntry.Text ?? "").Trim();

        var diffRaw = (DifficultyPicker.SelectedItem as string) ?? DifficultyPreset.Standard.ToString();
        Enum.TryParse(diffRaw, ignoreCase: true, out DifficultyPreset preset);

        var theme = (ThemePicker.SelectedItem as string) ?? "Default";

        _options.SetUsername(name);
        _options.SetStartingLevel(_startLevel);
        _options.SetDifficulty(preset);
        _options.SetTheme(theme);
        _options.SaveToPreferences();

        // Optional: keep panel closed after apply
        OptionsPanel.IsVisible = false;
        OptionsButton.Text = "Options";
    }

    // --- High scores (persisted) ---
    private async Task LoadHighScoresAsync()
    {
        try
        {
            var entries = await _highScoreStore.GetAsync();

            // Map to your existing UI row shape
            var rows = entries
                .OrderByDescending(e => e.Score)
                .ThenByDescending(e => e.Level)
                .ThenBy(e => e.Duration)
                .Select((e, idx) => new HighScoreRow
                {
                    Rank = idx + 1,
                    Name = string.IsNullOrWhiteSpace(e.PlayerName) ? "Player" : e.PlayerName,
                    Score = e.Score
                })
                .ToList();

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _scores.Clear();
                foreach (var r in rows)
                    _scores.Add(r);

                UpdateHighScoreEmptyState();
            });
        }
        catch
        {
            // Fail "quietly": keep UI responsive, show empty state.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _scores.Clear();
                UpdateHighScoreEmptyState();
            });
        }
    }

    private void UpdateHighScoreEmptyState()
    {
        HighScoresEmptyLabel.IsVisible = _scores.Count == 0;
        HighScoresList.IsVisible = _scores.Count > 0;
    }

    private async void OnClearHighScores(object sender, EventArgs e)
    {
        var ok = await DisplayAlert("Clear High Scores?", "This will remove all saved high scores on this device.", "Clear", "Cancel");
        if (!ok) return;

        try
        {
            await _highScoreStore.ClearAsync();
        }
        catch
        {
            await DisplayAlert("Could not clear", "Something went wrong clearing high scores.", "OK");
        }

        await LoadHighScoresAsync();
    }

    private async void OnHowToPlay(object sender, EventArgs e)
    {
        await DisplayAlert("How To Play",
            "Drop letter pieces to form valid words horizontally or vertically.\n" +
            "Every 5 words you’ll get a definition quiz.\n" +
            "Use Hold, Rotate, and Hard Drop to survive.",
            "OK");
    }

    // --- Play ---
    private async void OnPlay(object sender, EventArgs e)
    {
        var name = (UserEntry.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlert("Name required", "Please enter a username.", "OK");
            return;
        }

        // Save username + options (so Game can read it)
        var diffRaw = (DifficultyPicker.SelectedItem as string) ?? DifficultyPreset.Standard.ToString();
        Enum.TryParse(diffRaw, ignoreCase: true, out DifficultyPreset preset);
        var theme = (ThemePicker.SelectedItem as string) ?? "Default";

        _options.SetUsername(name);
        _options.SetStartingLevel(_startLevel);
        _options.SetDifficulty(preset);
        _options.SetTheme(theme);
        _options.SaveToPreferences();

        // Keep your existing restore gating key
        Preferences.Set("username", name);

        await Shell.Current.GoToAsync("//game");
    }

    // Small view model for the list
    public sealed class HighScoreRow
    {
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public int Score { get; set; }
    }
}