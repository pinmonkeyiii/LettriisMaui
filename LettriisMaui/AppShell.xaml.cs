using LettriisMaui.Views;

namespace LettriisMaui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("gameover", typeof(GameOverPage));
    }
}