namespace LettriisMaui.WinUI;

public partial class WinApp : MauiWinUIApplication
{
    public WinApp()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => LettriisMaui.MauiProgram.CreateMauiApp();
}