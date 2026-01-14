using System.Runtime.CompilerServices;

namespace LettriisMaui.Views;

public partial class LoadingPage : ContentPage
{
    public event Action? ContinueRequested;

    private string _statusText = "Loading…";
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value) return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private bool _canContinue;
    public bool CanContinue
    {
        get => _canContinue;
        set
        {
            if (_canContinue == value) return;
            _canContinue = value;
            OnPropertyChanged();
        }
    }

    public LoadingPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    public void MarkReady(string readyText = "Ready")
    {
        StatusText = readyText;
        CanContinue = true;
    }

    private void OnTapped(object sender, TappedEventArgs e)
    {
        if (!CanContinue) return;
        ContinueRequested?.Invoke();
    }
}