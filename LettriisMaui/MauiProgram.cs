using CommunityToolkit.Maui;
using LettriisMaui.Services;
using LettriisMaui.Services.GameResults;
using LettriisMaui.Services.HighScores;
using LettriisMaui.Services.Session;
using LettriisMaui.ViewModels;
using LettriisMaui.Views;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace LettriisMaui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                // fonts.AddFont("roboto-regular.ttf", "RobotoRegular");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<RandomService>();
        builder.Services.AddSingleton<WordListService>();
        builder.Services.AddSingleton<BannedWordsService>();
        builder.Services.AddSingleton<WordFilterService>();
        builder.Services.AddSingleton<DefinitionService>();
        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<AudioService>();
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton(AudioManager.Current);
        builder.Services.AddSingleton<IGameSessionStore, JsonGameSessionStore>();
        builder.Services.AddSingleton<IGameStartOptionsService, GameStartOptionsService>();
        builder.Services.AddSingleton<IHighScoreStore, JsonHighScoreStore>();
        builder.Services.AddSingleton<IGameResultBus, InMemoryGameResultBus>();

        builder.Services.AddTransient<GameViewModel>();
        builder.Services.AddTransient<LoadingPage>();
        builder.Services.AddTransient<StartPage>();
        builder.Services.AddTransient<GamePage>();
        builder.Services.AddTransient<GameOverViewModel>();
        builder.Services.AddTransient<GameOverPage>();

        return builder.Build();
    }
}
