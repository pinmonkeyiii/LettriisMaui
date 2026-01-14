// GamePage.xaml.cs
using LettriisMaui.Graphics;
using LettriisMaui.Services;
using LettriisMaui.ViewModels;
using System.ComponentModel;
using System.Diagnostics;

#if WINDOWS
using LettriisMaui.Models.Enums; // GameMode enum
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
#endif

namespace LettriisMaui.Views;

public partial class GamePage : ContentPage
{
    private readonly GameViewModel _vm;
    private readonly AudioService _audio;

    private readonly GameDrawable _drawable = new();
    private readonly PiecePreviewDrawable _nextDrawable = new();
    private readonly PiecePreviewDrawable _holdDrawable = new();

    // Overlay animation state
    private bool _overlayHandlersHooked;
    private bool _appearedOnce;

    // 9.4: moment routing (prevents firing on initial bind; keeps handlers clean)
    private readonly Dictionary<string, int> _momentSeen = new();

    // Per-effect cooldowns (keyed throttling so different effects can still run)
    private readonly Dictionary<string, long> _cooldownMs = new();
    private readonly Stopwatch _cooldownClock = Stopwatch.StartNew();

    // Music bucket tracking (prevents restarting each level)
    private int _lastMusicBucket = -1;

    private static readonly HashSet<string> BuiltInBackgroundFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "bg_lv_1_5.png",
        "bg_lv_6_10.png",
        "bg_lv_11_15.png",
        "bg_lv_16_20.png",
        "bg_lv_20_up.png",
    };

#if WINDOWS
    private UIElement? _nativeRoot;

    // DAS/ARR state
    private bool _leftHeld;
    private bool _rightHeld;
    private double _dasRemainingMs;
    private double _arrRemainingMs;

    private const double DAS_MS = 150; // tweak later
    private const double ARR_MS = 40;  // tweak later

    private IDispatcherTimer? _inputTimer;
    private readonly Stopwatch _inputClock = Stopwatch.StartNew();
    private long _inputLastMs;
#endif

    public GamePage(GameViewModel vm, AudioService audio)
    {
        InitializeComponent();

        _vm = vm;
        _audio = audio;

        BindingContext = _vm;

        GameView.Drawable = _drawable;

        NextPreview.Drawable = _nextDrawable;
        HoldPreview.Drawable = _holdDrawable;

        // subscribe/unsubscribe RequestRedraw on appear/disappear (avoid leaking pages)
    }

    // Save session on sleep (after pausing)
    private async void OnAppSleeping()
    {
        // Compatibility with your existing AppEvents path.
        // New GameLifecycle path also exists; this is redundant but harmless.
        try
        {
            _vm.StopAutoSave();
            if (_vm.IsPlaying)
                _vm.Pause("lifecycle");

            await _vm.SaveSessionAsync();
        }
        catch
        {
            // Sleeping should be resilient; swallow failures
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Step 9.10: subscribe redraw only while visible
        _vm.RequestRedraw += OnRequestRedraw;

        // Step 9.9: attach lifecycle hooks so App.OnSleep/OnResume broadcasts pause/resume safely
        _vm.AttachLifecycleHooks();

        // Step 9.10: nav-safe "shown"
        _vm.OnPageShown();

        AppEvents.AppSleeping += OnAppSleeping;

        // Hook MAUI window lifecycle
        if (Window is not null)
        {
            Window.Activated += OnWindowActivated;
            Window.Deactivated += OnWindowDeactivated;
        }

        HookOverlayAnimations();

        await _vm.InitializeAsync();

        // Turn on auto-save functionality
        _vm.StartAutoSave();

        // Seed moment counters so we don't fire juice/audio on first bind
        SeedMomentCounters();

        // Ensure first paint
        OnRequestRedraw();

        // Start autosave loop only when page is active + initialized
        _vm.StartAutoSave();

        // Ensure background is set immediately for current level
        UpdateBackgroundForLevel();

        // Ensure we can always start music even if bucket gating would prevent it (e.g., toggled off then on)
        if (_vm.MusicEnabled)
            _lastMusicBucket = -1;

        // 9.6: Select correct track + enforce correct play/pause state
        _ = EnsureMusicCorrectAsync(forceRestart: false);

        // Snap overlays to correct initial state (no "fade-in" on first navigation)
        if (!_appearedOnce)
        {
            _appearedOnce = true;
            SnapOverlayToState();

            if (LevelFlash is not null)
                LevelFlash.Opacity = 0;
        }
        else
        {
            _ = AnimateOverlaysToStateAsync();
        }

#if WINDOWS
        // Ensure focus after navigation
        Dispatcher.Dispatch(() =>
        {
            if (Handler?.PlatformView is FrameworkElement fe)
                fe.Focus(FocusState.Programmatic);
        });
#endif
    }

    protected override async void OnDisappearing()
    {
        // nav-safe "hidden" (pause game due to navigation)
        _vm.OnPageHidden();

        // Stop autosave first so it can't overlap with the final save
        _vm.StopAutoSave();

        // Save session after applying nav pause
        try
        {
            await _vm.SaveSessionAsync();
        }
        catch
        {
            // Swallow; disappearing should be resilient
        }

        // 9.6: intentionally pause music when leaving GamePage
        _audio.PauseMusic();

        // Step 9.9: detach lifecycle hooks so hidden pages don’t receive broadcasts
        _vm.DetachLifecycleHooks();

        AppEvents.AppSleeping -= OnAppSleeping;

        // Step 9.10: unsubscribe redraw to avoid leaking the page
        _vm.RequestRedraw -= OnRequestRedraw;

        UnhookOverlayAnimations();

        // Unhook MAUI window lifecycle
        if (Window is not null)
        {
            Window.Activated -= OnWindowActivated;
            Window.Deactivated -= OnWindowDeactivated;
        }

#if WINDOWS
        if (_nativeRoot is not null)
        {
            _nativeRoot.KeyDown -= OnNativeKeyDown;
            _nativeRoot.KeyUp -= OnNativeKeyUp;
            _nativeRoot = null;
        }

        if (_inputTimer is not null)
        {
            _inputTimer.Stop();
            _inputTimer = null;
        }

        ClearHeldHorizontal();
#endif

        base.OnDisappearing();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

#if WINDOWS
        HookWindowsKeys();
        StartInputLoop();
#endif
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (Handler is null) return;
        _ = EnsureMusicCorrectAsync(forceRestart: false);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
#if WINDOWS
        PauseFromSystem();
#endif
        _audio.PauseMusic();
    }

    private void OnRequestRedraw()
    {
        _drawable.RenderState = _vm.CurrentRenderState;
        GameView.Invalidate();

        _nextDrawable.Piece = _vm.NextPieceRender;
        NextPreview.Invalidate();

        _holdDrawable.Piece = _vm.HoldPieceRender;
        HoldPreview.Invalidate();
    }

    // --- On-screen button handlers ---
    private void Left(object sender, EventArgs e) => _vm.MoveLeft();
    private void Right(object sender, EventArgs e) => _vm.MoveRight();

    private void Rotate(object sender, EventArgs e)
    {
        _vm.Rotate();
        _ = Sfx("rotate", throttleMs: 45);
    }

    private void Hold(object sender, EventArgs e) => _vm.HoldSwap();

    private void Drop(object sender, EventArgs e)
    {
        _vm.HardDrop();
    }

    private void DownPressed(object sender, EventArgs e) => _vm.SetSoftDrop(true);
    private void DownReleased(object sender, EventArgs e) => _vm.SetSoftDrop(false);

    // Settings focus recovery + music restart (wire these in XAML)
    // <Switch ... Toggled="OnSettingsToggled" Unfocused="OnSettingsUnfocused" />
    private void OnSettingsToggled(object sender, ToggledEventArgs e)
    {
#if WINDOWS
        // Switch can steal keyboard focus; return it to the game
        ClearHeldHorizontal();
        RestoreGameFocus();
#endif

        // If Music was toggled back ON, bucket gating can prevent restart
        // (music player was stopped when disabled).
        if (sender == MusicSwitch && e.Value)
        {
            _lastMusicBucket = -1;
            _ = EnsureMusicCorrectAsync(forceRestart: true);
        }
    }

    private void OnSettingsUnfocused(object sender, FocusEventArgs e)
    {
#if WINDOWS
        ClearHeldHorizontal();
        RestoreGameFocus();
#endif
    }

    // Pause/GameOver overlay buttons
    private void ResumeClicked(object sender, EventArgs e)
    {
        _vm.Resume("user");
        _ = EnsureMusicCorrectAsync(forceRestart: false);

#if WINDOWS
        ClearHeldHorizontal();
        RestoreGameFocus();
#endif
    }

    private void RestartClicked(object sender, EventArgs e)
    {
        _vm.Restart();

        UpdateBackgroundForLevel();

        // After restart, always re-evaluate track
        _lastMusicBucket = -1;
        _ = EnsureMusicCorrectAsync(forceRestart: true);

#if WINDOWS
        ClearHeldHorizontal();
        RestoreGameFocus();
#endif
    }

    private void PickChoice(object sender, EventArgs e)
    {
        if (sender is Button b && b.BindingContext is string choice)
            _vm.QuizPick(choice);

        _ = EnsureMusicCorrectAsync(forceRestart: false);

#if WINDOWS
        RestoreGameFocus();
#endif
    }

    public async void PauseFromAppSleep()
    {
        try
        {
            if (_vm.IsPlaying) _vm.Pause("lifecycle");
            await _vm.SaveSessionAsync();
        }
        catch { /* ignore */ }

        _ = EnsureMusicCorrectAsync(forceRestart: false);
    }

    private void SkipQuiz(object sender, EventArgs e)
    {
        _vm.QuizSkip();
        _ = EnsureMusicCorrectAsync(forceRestart: false);

#if WINDOWS
        RestoreGameFocus();
#endif
    }

    // -----------------------------
    // 9.6 Music lifecycle helpers
    // -----------------------------

    private bool IsGameplayActive()
        => _vm.IsPlaying && !_vm.IsPaused && !_vm.IsGameOver && !_vm.IsQuizVisible;

    private async Task EnsureMusicCorrectAsync(bool forceRestart = false)
    {
        // If music is disabled, ensure it's stopped and don't try to play/resume
        if (!_vm.MusicEnabled)
        {
            _audio.StopMusic();
            return;
        }

        await UpdateMusicForLevel(_vm.Level);

        if (!IsGameplayActive())
        {
            _audio.PauseMusic();
            return;
        }

        if (forceRestart)
            await _audio.PlayMusicAsync(AudioService.GetMusicKeyForLevel(_vm.Level), restartIfSame: true);
        else
            _audio.ResumeMusic();
    }

    // -----------------------------
    // Overlay animation plumbing
    // -----------------------------

    private void HookOverlayAnimations()
    {
        if (_overlayHandlersHooked)
            return;

        if (_vm is INotifyPropertyChanged npc)
            npc.PropertyChanged += OnVmPropertyChanged;

        _overlayHandlersHooked = true;
    }

    private void UnhookOverlayAnimations()
    {
        if (!_overlayHandlersHooked)
            return;

        if (_vm is INotifyPropertyChanged npc)
            npc.PropertyChanged -= OnVmPropertyChanged;

        _overlayHandlersHooked = false;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameViewModel.IsPaused) ||
            e.PropertyName == nameof(GameViewModel.IsQuizVisible))
        {
            _ = EnsureMusicCorrectAsync(forceRestart: false);
            _ = AnimateOverlaysToStateAsync();
            return;
        }

        if (e.PropertyName == nameof(GameViewModel.IsGameOver))
        {
            _audio.PauseMusic();
            return;
        }

        if (e.PropertyName == nameof(GameViewModel.MusicEnabled))
        {
            // If music got stopped when disabled, bucket gating would prevent restart.
            _lastMusicBucket = -1;
            _ = EnsureMusicCorrectAsync(forceRestart: true);
            return;
        }

        if (e.PropertyName == nameof(GameViewModel.Level))
        {
            UpdateBackgroundForLevel();
            _ = EnsureMusicCorrectAsync(forceRestart: false);
            return;
        }

        if (e.PropertyName == nameof(GameViewModel.LockMoment))
        {
            if (HasMomentAdvanced(e.PropertyName!, _vm.LockMoment))
                _ = OnMomentAsync("Lock", minGapMs: 30);
        }
        else if (e.PropertyName == nameof(GameViewModel.ClearMoment))
        {
            if (HasMomentAdvanced(e.PropertyName!, _vm.ClearMoment))
                _ = OnMomentAsync("Clear", minGapMs: 60);
        }
        else if (e.PropertyName == nameof(GameViewModel.BigClearMoment))
        {
            if (HasMomentAdvanced(e.PropertyName!, _vm.BigClearMoment))
                _ = OnMomentAsync("BigClear", minGapMs: 90);
        }
        else if (e.PropertyName == nameof(GameViewModel.LevelUpMoment))
        {
            if (HasMomentAdvanced(e.PropertyName!, _vm.LevelUpMoment))
                _ = OnMomentAsync("LevelUp", minGapMs: 120);
        }
        else if (e.PropertyName == nameof(GameViewModel.QuizCorrectMoment))
        {
            if (HasMomentAdvanced(e.PropertyName!, _vm.QuizCorrectMoment))
                _ = OnMomentAsync("QuizCorrect", minGapMs: 120);
        }
        else if (e.PropertyName == nameof(GameViewModel.QuizWrongMoment))
        {
            if (HasMomentAdvanced(e.PropertyName!, _vm.QuizWrongMoment))
                _ = OnMomentAsync("QuizWrong", minGapMs: 120);
        }
    }

    private void SeedMomentCounters()
    {
        _momentSeen[nameof(GameViewModel.LockMoment)] = _vm.LockMoment;
        _momentSeen[nameof(GameViewModel.ClearMoment)] = _vm.ClearMoment;
        _momentSeen[nameof(GameViewModel.BigClearMoment)] = _vm.BigClearMoment;
        _momentSeen[nameof(GameViewModel.LevelUpMoment)] = _vm.LevelUpMoment;
        _momentSeen[nameof(GameViewModel.QuizCorrectMoment)] = _vm.QuizCorrectMoment;
        _momentSeen[nameof(GameViewModel.QuizWrongMoment)] = _vm.QuizWrongMoment;
    }

    private bool HasMomentAdvanced(string propName, int currentValue)
    {
        if (!_momentSeen.TryGetValue(propName, out var last))
        {
            _momentSeen[propName] = currentValue;
            return false;
        }

        if (currentValue == last)
            return false;

        _momentSeen[propName] = currentValue;
        return true;
    }

    private bool IsCooledDown(string key, int minGapMs)
    {
        var now = _cooldownClock.ElapsedMilliseconds;

        if (_cooldownMs.TryGetValue(key, out var last) && (now - last) < minGapMs)
            return false;

        _cooldownMs[key] = now;
        return true;
    }

    private async Task OnMomentAsync(string moment, int minGapMs)
    {
        if (Handler is null)
            return;

        if (_vm.IsGameOver)
            return;

        if (!IsCooledDown("moment:" + moment, minGapMs))
            return;

        try
        {
            switch (moment)
            {
                case "Lock":
                    await DoJuiceLockAsync();
                    break;

                case "Clear":
                    await Task.WhenAll(
                        DoJuiceClearAsync(big: false),
                        _audio.DuckMusicAsync(multiplier: 0.80, downMs: 15, holdMs: 80, upMs: 120),
                        Sfx("clear_word", throttleMs: 60),
                        PulseHudForClearAsync(big: false)
                    );
                    break;

                case "BigClear":
                    await Task.WhenAll(
                        DoJuiceClearAsync(big: true),
                        _audio.DuckMusicAsync(multiplier: 0.60, downMs: 25, holdMs: 160, upMs: 140),
                        Sfx("clear_word", throttleMs: 80),
                        PulseHudForClearAsync(big: true)
                    );
                    break;

                case "LevelUp":
                    await Task.WhenAll(
                        DoJuiceLevelUpAsync(),
                        _audio.DuckMusicAsync(multiplier: 0.45, downMs: 30, holdMs: 220, upMs: 160),
                        Sfx("level_up", throttleMs: 200),
                        FlashLevelUpAsync(),
                        PulseHudForLevelUpAsync()
                    );
                    break;

                case "QuizCorrect":
                    await DoJuiceQuizResultAsync(correct: true);
                    break;

                case "QuizWrong":
                    await DoJuiceQuizResultAsync(correct: false);
                    break;
            }
        }
        catch
        {
            // swallow animation cancellations during navigation
        }
    }

    private Task DoJuiceLockAsync()
    {
        var root = GameAreaRoot;
        if (root is null) return Task.CompletedTask;
        return BumpAsync(root, y: 3, inMs: 55, outMs: 110);
    }

    private Task DoJuiceClearAsync(bool big)
    {
        var root = GameAreaRoot;
        if (root is null) return Task.CompletedTask;

        if (!big)
            return BumpAsync(root, y: 6, inMs: 60, outMs: 130);

        return Task.WhenAll(
            BumpAsync(root, y: 10, inMs: 70, outMs: 160),
            PulseAsync(root, minScale: 0.995, maxScale: 1.01, ms: 140)
        );
    }

    private Task DoJuiceLevelUpAsync()
    {
        var root = GameAreaRoot;
        if (root is null) return Task.CompletedTask;

        return Task.WhenAll(
            BumpAsync(root, y: 12, inMs: 75, outMs: 180),
            PulseAsync(root, minScale: 0.995, maxScale: 1.02, ms: 180)
        );
    }

    private Task DoJuiceQuizResultAsync(bool correct)
    {
        var root = GameAreaRoot;
        if (root is null) return Task.CompletedTask;

        if (correct)
            return PulseAsync(root, minScale: 0.995, maxScale: 1.015, ms: 160);

        return ShakeXAsync(root, amplitude: 10, shakes: 4, msPerHalf: 35);
    }

    private Task FlashLevelUpAsync()
    {
        if (!IsCooledDown("vfx:level_flash", 220))
            return Task.CompletedTask;

        if (LevelFlash is null)
            return Task.CompletedTask;

        return RunOnUiAsync(async () =>
        {
            try
            {
                LevelFlash.AbortAnimation("level_flash");
                LevelFlash.Opacity = 0;

                await LevelFlash.FadeTo(0.16, 70, Easing.CubicOut);
                await LevelFlash.FadeTo(0.00, 180, Easing.CubicIn);
            }
            catch
            {
                LevelFlash.Opacity = 0;
            }
        });
    }

    private Task PulseHudForClearAsync(bool big)
    {
        if (!IsCooledDown(big ? "hud:clear_big" : "hud:clear", big ? 160 : 120))
            return Task.CompletedTask;

        return RunOnUiAsync(async () =>
        {
            if (ScoreValue is not null)
                await PulseLabelAsync(ScoreValue, maxScale: big ? 1.10 : 1.06, upMs: 70, downMs: 120);

            if (ComboValue is not null && ComboValue.IsVisible)
                await PulseLabelAsync(ComboValue, maxScale: big ? 1.14 : 1.08, upMs: 70, downMs: 140);
        });
    }

    private Task PulseHudForLevelUpAsync()
    {
        if (!IsCooledDown("hud:level_up", 220))
            return Task.CompletedTask;

        return RunOnUiAsync(async () =>
        {
            if (LevelValue is not null)
                await PulseLabelAsync(LevelValue, maxScale: 1.14, upMs: 80, downMs: 160);

            if (ScoreValue is not null)
                await PulseLabelAsync(ScoreValue, maxScale: 1.07, upMs: 70, downMs: 140);
        });
    }

    private static async Task PulseLabelAsync(VisualElement el, double maxScale, uint upMs, uint downMs)
    {
        el.AbortAnimation("hud_pulse");
        el.Scale = 1;

        await el.ScaleTo(maxScale, upMs, Easing.CubicOut);
        await el.ScaleTo(1, downMs, Easing.CubicIn);
    }

    private Task RunOnUiAsync(Func<Task> work)
    {
        if (Handler is null)
            return Task.CompletedTask;

        try
        {
            Dispatcher.Dispatch(async () =>
            {
                try { await work(); }
                catch { /* ignore */ }
            });
        }
        catch
        {
            // If we're tearing down, just no-op
        }

        return Task.CompletedTask;
    }

    private void SnapOverlayToState()
    {
        SnapOverlay(PauseOverlay, _vm.IsPaused);
        SnapOverlay(GameOverOverlay, _vm.IsGameOver);
        SnapOverlay(QuizOverlay, _vm.IsQuizVisible);
    }

    private static void SnapOverlay(VisualElement overlay, bool show)
    {
        if (overlay is null) return;

        overlay.AbortAnimation("overlay");
        overlay.IsVisible = show;
        overlay.Opacity = show ? 1 : 0;
        overlay.Scale = show ? 1 : 0.985;
    }

    private async Task AnimateOverlaysToStateAsync()
    {
        await AnimateOverlayAsync(PauseOverlay, _vm.IsPaused);
        await AnimateOverlayAsync(GameOverOverlay, _vm.IsGameOver);
        await AnimateOverlayAsync(QuizOverlay, _vm.IsQuizVisible);
    }

    private static async Task AnimateOverlayAsync(VisualElement overlay, bool show)
    {
        if (overlay is null) return;

        if (overlay.Handler is null)
        {
            SnapOverlay(overlay, show);
            return;
        }

        overlay.AbortAnimation("overlay");

        if (show)
        {
            overlay.IsVisible = true;
            overlay.Opacity = 0;
            overlay.Scale = 0.985;

            try
            {
                await Task.WhenAll(
                    overlay.FadeTo(1, 160, Easing.CubicOut),
                    overlay.ScaleTo(1, 180, Easing.CubicOut)
                );
            }
            catch
            {
                overlay.Opacity = 1;
                overlay.Scale = 1;
            }
        }
        else
        {
            if (!overlay.IsVisible)
                return;

            try
            {
                await Task.WhenAll(
                    overlay.FadeTo(0, 140, Easing.CubicIn),
                    overlay.ScaleTo(0.985, 140, Easing.CubicIn)
                );
            }
            catch { /* ignore */ }

            overlay.IsVisible = false;
        }
    }

    private void UpdateBackgroundForLevel()
    {
        if (Bg is null)
            return;

        var themeLevelKey = GetThemeLevelKey(_vm.Level);
        if (TryGetResourceImage(themeLevelKey, out var themedLevelImage))
        {
            Bg.Source = themedLevelImage;
            return;
        }

        var builtInFile = GetBuiltInLevelFile(_vm.Level);
        if (BuiltInBackgroundFiles.Contains(builtInFile))
        {
            Bg.Source = ImageSource.FromFile(builtInFile);
            return;
        }

        if (TryGetResourceImage("GameBackgroundImage", out var themeDefault))
        {
            Bg.Source = themeDefault;
            return;
        }

        Bg.Source = null;
    }

    private static string GetThemeLevelKey(int level) => level switch
    {
        <= 5 => "GameBackgroundImage_Lv_1_5",
        <= 10 => "GameBackgroundImage_Lv_6_10",
        <= 15 => "GameBackgroundImage_Lv_11_15",
        <= 20 => "GameBackgroundImage_Lv_16_20",
        _ => "GameBackgroundImage_Lv_20_Plus",
    };

    private static string GetBuiltInLevelFile(int level) => level switch
    {
        <= 5 => "bg_lv_1_5.png",
        <= 10 => "bg_lv_6_10.png",
        <= 15 => "bg_lv_11_15.png",
        <= 20 => "bg_lv_16_20.png",
        _ => "bg_lv_20_up.png",
    };

    private bool TryGetResourceImage(string key, out ImageSource image)
    {
        image = null!;

        if (Resources.TryGetValue(key, out var res) && res is ImageSource img)
        {
            image = img;
            return true;
        }

        if (Microsoft.Maui.Controls.Application.Current?.Resources.TryGetValue(key, out var appRes) == true && appRes is ImageSource appImg)
        {
            image = appImg;
            return true;
        }

        return false;
    }

    private Task Sfx(string key, int throttleMs = 0, double? volume = null)
    {
        if (throttleMs > 0 && !IsCooledDown("sfx:" + key, throttleMs))
            return Task.CompletedTask;

        return _audio.PlaySfxAsync(key, volume: volume, throttleMs: throttleMs);
    }

    private async Task UpdateMusicForLevel(int level)
    {
        var bucket = (level <= 0) ? 0 : (level - 1) / 5;

        // If bucket hasn't changed, we normally don't restart music.
        // BUT if music was toggled off then back on, the player was stopped,
        // so we must allow a re-play even within the same bucket.
        if (bucket == _lastMusicBucket)
        {
            // We'll still try to ensure music exists when enabled.
            // Force a play via EnsureMusicCorrectAsync(forceRestart:true) path,
            // which resets _lastMusicBucket to -1 before calling this.
            return;
        }

        _lastMusicBucket = bucket;

        var key = AudioService.GetMusicKeyForLevel(level);
        await _audio.PlayMusicAsync(key, restartIfSame: false);
    }

    private static async Task BumpAsync(VisualElement el, double y, uint inMs, uint outMs)
    {
        el.AbortAnimation("juice_bump");
        el.TranslationY = 0;

        await el.TranslateTo(0, y, inMs, Easing.CubicOut);
        await el.TranslateTo(0, 0, outMs, Easing.CubicIn);
    }

    private static async Task ShakeXAsync(VisualElement el, double amplitude, int shakes, uint msPerHalf)
    {
        el.AbortAnimation("juice_shake");
        el.TranslationX = 0;

        for (int i = 0; i < shakes; i++)
        {
            await el.TranslateTo(+amplitude, el.TranslationY, msPerHalf, Easing.CubicOut);
            await el.TranslateTo(-amplitude, el.TranslationY, msPerHalf, Easing.CubicOut);
        }

        await el.TranslateTo(0, el.TranslationY, msPerHalf, Easing.CubicIn);
    }

    private static async Task PulseAsync(VisualElement el, double minScale, double maxScale, uint ms)
    {
        el.AbortAnimation("juice_pulse");
        el.Scale = 1;

        await el.ScaleTo(maxScale, ms / 2, Easing.CubicOut);
        await el.ScaleTo(1, ms / 2, Easing.CubicIn);
    }

#if WINDOWS
    private void HookWindowsKeys()
    {
        if (_nativeRoot is not null)
        {
            _nativeRoot.KeyDown -= OnNativeKeyDown;
            _nativeRoot.KeyUp -= OnNativeKeyUp;
            _nativeRoot = null;
        }

        if (Handler?.PlatformView is FrameworkElement fe)
        {
            _nativeRoot = fe;
            _nativeRoot.KeyDown += OnNativeKeyDown;
            _nativeRoot.KeyUp += OnNativeKeyUp;

            fe.Loaded += (_, __) =>
            {
                fe.IsTabStop = true;
                fe.Focus(FocusState.Programmatic);
            };
        }
    }

    private void StartInputLoop()
    {
        if (_inputTimer is not null) return;

        _inputLastMs = _inputClock.ElapsedMilliseconds;

        _inputTimer = Dispatcher.CreateTimer();
        _inputTimer.Interval = TimeSpan.FromMilliseconds(16);
        _inputTimer.Tick += (_, __) =>
        {
            var now = _inputClock.ElapsedMilliseconds;
            var dt = (int)Math.Clamp(now - _inputLastMs, 0, 50);
            _inputLastMs = now;

            UpdateHeldHorizontal(dt);
        };
        _inputTimer.Start();
    }

    private void OnNativeKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool isRepeat = e.KeyStatus.RepeatCount > 1;

        switch (e.Key)
        {
            case VirtualKey.Escape:
            case VirtualKey.P:
                if (!isRepeat)
                {
                    if (_vm.IsGameOver) _vm.Restart();
                    else _vm.TogglePause();

                    ClearHeldHorizontal();
                    _ = EnsureMusicCorrectAsync(forceRestart: false);
                }
                e.Handled = true;
                break;

            case VirtualKey.R:
                if (!isRepeat)
                {
                    if (_vm.IsPaused || _vm.IsGameOver)
                    {
                        _vm.Restart();
                        ClearHeldHorizontal();
                        _ = EnsureMusicCorrectAsync(forceRestart: true);
                    }
                }
                e.Handled = true;
                break;

            case VirtualKey.Left:
                if (!_leftHeld)
                {
                    _leftHeld = true;
                    _rightHeld = false;
                    _dasRemainingMs = DAS_MS;
                    _arrRemainingMs = 0;
                    _vm.MoveLeft();
                }
                e.Handled = true;
                break;

            case VirtualKey.Right:
                if (!_rightHeld)
                {
                    _rightHeld = true;
                    _leftHeld = false;
                    _dasRemainingMs = DAS_MS;
                    _arrRemainingMs = 0;
                    _vm.MoveRight();
                }
                e.Handled = true;
                break;

            case VirtualKey.Down:
                _vm.SetSoftDrop(true);
                e.Handled = true;
                break;

            case VirtualKey.Up:
            case VirtualKey.X:
                if (!isRepeat)
                {
                    _vm.Rotate();
                    _ = Sfx("rotate", throttleMs: 45);
                }
                e.Handled = true;
                break;

            case VirtualKey.Space:
                if (!isRepeat)
                    _vm.HardDrop();
                e.Handled = true;
                break;

            case VirtualKey.C:
                if (!isRepeat) _vm.HoldSwap();
                e.Handled = true;
                break;
        }
    }

    private void OnNativeKeyUp(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left:
                _leftHeld = false;
                e.Handled = true;
                break;

            case VirtualKey.Right:
                _rightHeld = false;
                e.Handled = true;
                break;

            case VirtualKey.Down:
                _vm.SetSoftDrop(false);
                e.Handled = true;
                break;
        }
    }

    private void RestoreGameFocus()
    {
        Dispatcher.Dispatch(() =>
        {
            if (Handler?.PlatformView is FrameworkElement fe)
                fe.Focus(FocusState.Programmatic);
        });
    }

    private void UpdateHeldHorizontal(int dtMs)
    {
        if (!_vm.IsPlaying)
            return;

        if (!_leftHeld && !_rightHeld)
            return;

        if (_dasRemainingMs > 0)
        {
            _dasRemainingMs -= dtMs;
            return;
        }

        _arrRemainingMs -= dtMs;
        if (_arrRemainingMs > 0)
            return;

        _arrRemainingMs = ARR_MS;

        if (_leftHeld) _vm.MoveLeft();
        else if (_rightHeld) _vm.MoveRight();
    }

    private void ClearHeldHorizontal()
    {
        _leftHeld = false;
        _rightHeld = false;
        _dasRemainingMs = 0;
        _arrRemainingMs = 0;
    }

    private void PauseFromSystem()
    {
        if (_vm.Mode == GameMode.Playing)
        {
            _vm.Pause("system");
            ClearHeldHorizontal();
            _ = EnsureMusicCorrectAsync(forceRestart: false);
        }
    }
#endif
}