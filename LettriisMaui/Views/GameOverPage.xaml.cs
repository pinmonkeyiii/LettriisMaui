using LettriisMaui.ViewModels;
using Microsoft.Maui.Storage;

namespace LettriisMaui.Views;

public partial class GameOverPage : ContentPage
{
    private readonly GameOverViewModel _vm;

    public GameOverPage(GameOverViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        var username = Preferences.Get("username", "");
        _vm.LoadFromBus(username);

        // Small entrance polish (safe, optional)
        Opacity = 0;
        TranslationY = 10;
        await Task.WhenAll(
            this.FadeTo(1, 140, Easing.CubicOut),
            this.TranslateTo(0, 0, 140, Easing.CubicOut)
        );
    }
}