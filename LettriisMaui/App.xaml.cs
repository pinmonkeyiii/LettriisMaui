using LettriisMaui.Services;
using LettriisMaui.Views;
using System.Diagnostics;

namespace LettriisMaui;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly ThemeService _themeService;
    private Window? _window;

    public App(IServiceProvider services, ThemeService themeService)
    {
        InitializeComponent();

        _services = services;
        _themeService = themeService;

        // Apply theme before any UI loads DynamicResources
        _themeService.ApplySavedTheme();
    }

    protected override void OnSleep()
    {
        base.OnSleep();

        // Keep existing behavior
        AppEvents.RaiseAppSleeping();

        // Step 9.9: lifecycle broadcast (safe even if nobody is listening)
        GameLifecycle.RequestPause("app_sleep");
    }

    protected override void OnResume()
    {
        base.OnResume();

        // If you already have an AppEvents resume equivalent, call it here too
        // AppEvents.RaiseAppResumed();

        GameLifecycle.RequestResume("app_resume");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var loading = _services.GetRequiredService<LoadingPage>();
        _window = new Window(loading);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await BootAndContinueAsync(loading);
        });

        return _window;
    }

    private async Task BootAndContinueAsync(LoadingPage loadingPage)
    {
        loadingPage.StatusText = "Loading word list…";
        var wordList = _services.GetRequiredService<WordListService>();
        _ = await wordList.GetCommonWordsAsync();

        loadingPage.StatusText = "Loading audio…";
        var audio = _services.GetRequiredService<AudioService>();

        var settings = _services.GetRequiredService<SettingsService>();
        audio.ApplySettings(settings.SfxEnabled, settings.MusicEnabled);
        Debug.WriteLine($"[AUDIO] Boot settings: Sfx={settings.SfxEnabled} Music={settings.MusicEnabled}");

        await audio.PreloadAsync(new[] { "rotate", "clear_word", "level_up" });

        loadingPage.MarkReady("Ready!");

        var tcs = new TaskCompletionSource();
        void OnContinue() => tcs.TrySetResult();
        loadingPage.ContinueRequested += OnContinue;
        await tcs.Task;
        loadingPage.ContinueRequested -= OnContinue;

        var shell = _services.GetRequiredService<AppShell>();
        _window!.Page = shell;

        await shell.GoToAsync("//start");
    }
}