using System.Windows.Input;
using LettriisMaui.Models;
using LettriisMaui.Services.GameResults;
using LettriisMaui.Services.HighScores;

namespace LettriisMaui.ViewModels;

public sealed class GameOverViewModel : BindableObject
{
    private readonly IGameResultBus _resultBus;
    private readonly IHighScoreStore _highScoreStore;

    private GameResult? _result;
    public GameResult? Result
    {
        get => _result;
        private set
        {
            _result = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(ScoreText));
            OnPropertyChanged(nameof(LevelText));
            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(LinesText));
            OnPropertyChanged(nameof(WordsText));
        }
    }

    public bool HasResult => Result != null;

    private string _playerName = "";
    public string PlayerName
    {
        get => _playerName;
        set { _playerName = value; OnPropertyChanged(); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set { _isBusy = value; OnPropertyChanged(); ((Command)SubmitCommand).ChangeCanExecute(); }
    }

    private bool _submitted;
    public bool Submitted
    {
        get => _submitted;
        private set
        {
            _submitted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SubmitButtonText));
            OnPropertyChanged(nameof(SubmittedMessage));
            ((Command)SubmitCommand).ChangeCanExecute();
        }
    }

    public string ScoreText => Result?.Score.ToString() ?? "—";
    public string LevelText => Result?.Level.ToString() ?? "—";
    public string DurationText => Result?.Duration.ToString() ?? "—";
    public string LinesText => Result?.Lines.ToString() ?? "—";
    public string WordsText => Result?.WordsCleared.ToString() ?? "—";

    public string SubmitButtonText => Submitted ? "Submitted" : "Submit";
    public string SubmittedMessage => Submitted ? "Saved to High Scores ✅" : "";

    public ICommand SubmitCommand { get; }
    public ICommand PlayAgainCommand { get; }
    public ICommand BackToStartCommand { get; }

    public GameOverViewModel(IGameResultBus resultBus, IHighScoreStore highScoreStore)
    {
        _resultBus = resultBus;
        _highScoreStore = highScoreStore;

        SubmitCommand = new Command(async () => await SubmitAsync(), () => !IsBusy && !Submitted && HasResult);
        PlayAgainCommand = new Command(async () => await Shell.Current.GoToAsync("//game"));
        BackToStartCommand = new Command(async () => await Shell.Current.GoToAsync("//start"));
    }

    public void LoadFromBus(string? defaultPlayerName = null)
    {
        Result = _resultBus.GetAndClear();
        Submitted = false;

        if (!string.IsNullOrWhiteSpace(defaultPlayerName))
            PlayerName = defaultPlayerName.Trim();

        ((Command)SubmitCommand).ChangeCanExecute();
    }

    private async Task SubmitAsync()
    {
        if (IsBusy || Submitted || Result == null)
            return;

        var name = (PlayerName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "Player";

        IsBusy = true;
        try
        {
            var entry = new HighScoreEntry
            {
                PlayerName = name,
                Score = Result.Score,
                Level = Result.Level,
                Lines = Result.Lines,
                WordsCleared = Result.WordsCleared,
                Duration = Result.Duration,
                AchievedAt = Result.EndedAt,
                ModeKey = Result.ModeKey,
                OptionsHash = Result.OptionsHash
            };

            await _highScoreStore.SubmitAsync(entry);
            Submitted = true;
        }
        finally
        {
            IsBusy = false;
        }
    }
}