using LettriisMaui.Models;
using LettriisMaui.Models.Enums;
using LettriisMaui.Models.Rendering;
using LettriisMaui.Models.Session;
using LettriisMaui.Services;
using LettriisMaui.Services.GameResults;
using LettriisMaui.Services.Session;
using Microsoft.Maui.Storage;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace LettriisMaui.ViewModels;

public sealed class GameViewModel : INotifyPropertyChanged
{
    private readonly RandomService _rng;
    private readonly WordListService _wordList;
    private readonly WordFilterService _filter;
    private readonly DefinitionService _defs;
    private readonly AudioService _audio;
    private readonly SettingsService _settings;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastTickMs = 0;

    private readonly IGameStartOptionsService _startOptions;
    private readonly IGameResultBus _resultBus;

    // One-time init guard (Step 9.10)
    private bool _initialized;
    public bool IsInitialized => _initialized;

    // HUD change tracking
    private int _lastScore;
    private int _lastLevel;
    private int _lastMinLen;
    private bool _lastNoRepeats;

    // Clear flash
    private List<GridCell> _flashCells = new();
    private int _flashRemainingMs = 0;
    private const int ClearFlashMs = 250;

    // Timer
    private IDispatcherTimer? _timer;
    private bool _timerHooked = false;

    // Motion
    private double _fallAccS = 0;
    private bool _softDrop = false;

    // Prevent overlapping resolve loops
    private bool _resolving = false;

    // Mode
    private GameMode _mode = GameMode.Playing;

    // "moment counters"
    private int _lockMoment;
    private int _clearMoment;
    private int _bigClearMoment;
    private int _levelUpMoment;
    private int _quizCorrectMoment;
    private int _quizWrongMoment;

    // lifecycle + pause reason gating
    private readonly object _pauseReasonLock = new();
    private readonly HashSet<string> _pauseReasons = new(StringComparer.OrdinalIgnoreCase);
    private bool _lifecycleHooksAttached;

    // Saving/restoring
    private readonly IGameSessionStore _sessionStore;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private bool _restoreAttempted;
    private bool _isApplyingRestoredState;

    // Save tuning
    private static readonly TimeSpan AutoSaveTick = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MinSaveInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(1.5);
    private CancellationTokenSource? _autoSaveCts;
    private bool _isSessionDirty;
    private bool _isSaveDisabled; // set true during restart/reset/apply restore to avoid weird mid-transition saves
    private DateTimeOffset _lastSavedAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastDirtyAt = DateTimeOffset.MinValue;

    private DateTimeOffset _runStartedAtUtc = DateTimeOffset.UtcNow;

    private bool HasPauseReasons
    {
        get { lock (_pauseReasonLock) return _pauseReasons.Count > 0; }
    }

    private void AddPauseReason(string reason)
    {
        lock (_pauseReasonLock) _pauseReasons.Add(reason);
    }

    private void RemovePauseReason(string reason)
    {
        lock (_pauseReasonLock) _pauseReasons.Remove(reason);
    }

    private bool HasPauseReason(string reason)
    {
        lock (_pauseReasonLock) return _pauseReasons.Contains(reason);
    }

    private void MarkSessionDirty()
    {
        _isSessionDirty = true;
        _lastDirtyAt = DateTimeOffset.UtcNow;
    }

    public void StartAutoSave()
    {
        if (_autoSaveCts != null) return;

        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(AutoSaveTick);
                while (await timer.WaitForNextTickAsync(token))
                {
                    await TrySaveSessionAsync("AutosaveTick", token);
                }
            }
            catch (OperationCanceledException) { /* ignore */ }
        }, token);
    }

    public void StopAutoSave()
    {
        try { _autoSaveCts?.Cancel(); }
        catch { /* ignore */ }
        finally
        {
            _autoSaveCts?.Dispose();
            _autoSaveCts = null;
        }
    }

    private async Task TrySaveSessionAsync(string reason, CancellationToken ct = default)
    {
        // Basic eligibility checks
        if (_isSaveDisabled) return;
        if (!_isSessionDirty) return;
        if (IsGameOver) return;
        if (IsQuizVisible) return;
        if (!_initialized) return;
        if (_isApplyingRestoredState) return;
        if (_resolving) return;

        var username = Preferences.Default.Get("username", string.Empty);
        if (string.IsNullOrWhiteSpace(username)) return;

        var now = DateTimeOffset.UtcNow;

        // Debounce: wait a moment after last dirty event so we save a stable snapshot
        if (now - _lastDirtyAt < DebounceWindow) return;

        // Throttle: don’t save too frequently
        if (now - _lastSavedAt < MinSaveInterval) return;

        await _sessionGate.WaitAsync(ct);
        try
        {
            // Re-check inside the gate (state may have changed while waiting)
            if (_isSaveDisabled) return;
            if (!_isSessionDirty) return;
            if (IsGameOver) return;

            var dto = BuildSessionDto();
            dto.SavedAt = DateTimeOffset.UtcNow;

            await _sessionStore.SaveAsync(dto, ct);
            _lastSavedAt = dto.SavedAt;
            _isSessionDirty = false;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GameState State { get; } = new();

    public ImageSource? LevelBackgroundImage
    {
        get => _levelBackgroundImage;
        private set { _levelBackgroundImage = value; OnPropertyChanged(); }
    }
    private ImageSource? _levelBackgroundImage;

    public ComboManager Combo { get; } = new();

    private HashSet<string> _commonWords = new(StringComparer.OrdinalIgnoreCase);

    public int Score => State.Score;
    public int Level => State.Level;
    public int MinLen => GameConstants.MinWordLength(State.Level);
    public bool NoRepeats => MinLen >= 5;

    public ObservableCollection<string> RecentWords { get; } = new();

    // 9.3: public moment properties (GamePage listens for PropertyChanged on these)
    public int LockMoment
    {
        get => _lockMoment;
        private set { _lockMoment = value; OnPropertyChanged(); }
    }

    public int ClearMoment
    {
        get => _clearMoment;
        private set { _clearMoment = value; OnPropertyChanged(); }
    }

    public int BigClearMoment
    {
        get => _bigClearMoment;
        private set { _bigClearMoment = value; OnPropertyChanged(); }
    }

    public int LevelUpMoment
    {
        get => _levelUpMoment;
        private set { _levelUpMoment = value; OnPropertyChanged(); }
    }

    public int QuizCorrectMoment
    {
        get => _quizCorrectMoment;
        private set { _quizCorrectMoment = value; OnPropertyChanged(); }
    }

    public int QuizWrongMoment
    {
        get => _quizWrongMoment;
        private set { _quizWrongMoment = value; OnPropertyChanged(); }
    }

    // Mode bindings
    public GameMode Mode
    {
        get => _mode;
        private set
        {
            if (_mode == value) return;
            _mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsGameOver));
        }
    }

    public bool IsPlaying => Mode == GameMode.Playing;
    public bool IsPaused => Mode == GameMode.Paused;
    public bool IsGameOver => Mode == GameMode.GameOver;
    public int BestScore => _settings.BestScore;

    private bool CanPlayInput => Mode == GameMode.Playing && !_resolving;

    public bool IsQuizVisible
    {
        get => _isQuizVisible;
        private set { _isQuizVisible = value; OnPropertyChanged(); }
    }
    private bool _isQuizVisible;

    public string QuizWord
    {
        get => _quizWord;
        private set { _quizWord = value; OnPropertyChanged(); }
    }
    private string _quizWord = "";

    public ObservableCollection<string> QuizChoices { get; } = new();
    private string _correctChoice = "";

    public event Action? RequestRedraw;
    public event Func<Task>? RequestGameOver;

    public RenderPiece? NextPieceRender { get; private set; }
    public RenderPiece? HoldPieceRender { get; private set; }
    public double ComboMultiplier => Combo.ComboMult;
    public bool IsComboActive => Combo.IsActive;

    public GameRenderState CurrentRenderState { get; private set; } =
        new GameRenderState
        {
            Columns = GameConstants.Cols,
            Rows = GameConstants.Rows,
            BoardLetters = new char[GameConstants.Rows, GameConstants.Cols],
            ActivePiece = null
        };

    public GameViewModel(
        RandomService rng,
        WordListService wordList,
        WordFilterService filter,
        DefinitionService defs,
        AudioService audio,
        SettingsService settings,
        IGameSessionStore sessionStore,
        IGameStartOptionsService startOptions,
        IGameResultBus resultBus)
    {
        _rng = rng;
        _wordList = wordList;
        _filter = filter;
        _defs = defs;
        _audio = audio;
        _settings = settings;
        _sessionStore = sessionStore;
        _startOptions = startOptions;
        _resultBus = resultBus;
    }

    // Step 9.9: call from GamePage.OnAppearing / OnDisappearing
    public void AttachLifecycleHooks()
    {
        if (_lifecycleHooksAttached) return;
        _lifecycleHooksAttached = true;

        GameLifecycle.PauseRequested += OnLifecyclePauseRequested;
        GameLifecycle.ResumeRequested += OnLifecycleResumeRequested;
    }

    public void DetachLifecycleHooks()
    {
        if (!_lifecycleHooksAttached) return;
        _lifecycleHooksAttached = false;

        GameLifecycle.PauseRequested -= OnLifecyclePauseRequested;
        GameLifecycle.ResumeRequested -= OnLifecycleResumeRequested;
    }

    private void OnLifecyclePauseRequested(string reason)
    {
        // If already paused/quiz/gameover, just record it.
        AddPauseReason("lifecycle");

        // Only transition if actively playing.
        if (Mode == GameMode.Playing)
            Pause("lifecycle");
    }

    private void OnLifecycleResumeRequested(string reason)
    {
        RemovePauseReason("lifecycle");

        // If anything else is still pausing us, do nothing.
        if (HasPauseReasons) return;

        // Only resume from Paused; never force out of Quiz/GameOver.
        if (Mode == GameMode.Paused)
            Resume("lifecycle");
    }

    // Step 9.10: nav-safe show/hide
    public void OnPageHidden()
    {
        // If leaving the page while playing, pause but mark as navigation pause.
        AddPauseReason("nav");

        if (Mode == GameMode.Playing)
            Pause("nav");
    }

    public void OnPageShown()
    {
        RemovePauseReason("nav");

        // Only resume if we are paused AND nothing else is pausing.
        if (Mode == GameMode.Paused && !HasPauseReasons)
            Resume("nav");
    }

    public async Task InitializeAsync()
    {
        // ensure init only runs once per VM instance
        if (_initialized) return;
        _initialized = true;

        _commonWords = await _wordList.GetCommonWordsAsync();
        Debug.WriteLine($"Common words loaded: {_commonWords.Count}");

        // 9.11: try restore session before starting a fresh run
        bool restored = false;
        try
        {
            restored = await TryRestoreSessionAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            restored = false;
        }

        if (!restored)
        {
            // No valid session -> start fresh (Restart already clears any session file)
            Restart();

            ApplyStartOptionsToNewRun();

            void ApplyStartOptionsToNewRun()
            {
                var opt = _startOptions.Current;

                // Starting level
                State.Level = Math.Clamp(opt.StartingLevel, 1, 20);

                // Difficulty presets -> tune starting gravity + maybe starting rows
                switch (opt.Difficulty)
                {
                    case DifficultyPreset.Casual:
                        State.GravityIntervalMs = 750;
                        break;
                    case DifficultyPreset.Standard:
                        State.GravityIntervalMs = 600;
                        break;
                    case DifficultyPreset.Hard:
                        State.GravityIntervalMs = 480;
                        break;
                    case DifficultyPreset.Insane:
                        State.GravityIntervalMs = 380;
                        break;
                }

                UpdateLevelBackground();
                UpdateHudIfChanged();
                Redraw();
            }
        }
        else
        {
            // Restored session has been applied by ApplySessionDto()
            _softDrop = false;
            _fallAccS = 0;

            // make sure HUD bindings are correct after restore
            UpdateHudIfChanged();
            Redraw();

            // If nothing is actively pausing us, resume gameplay
            if (Mode == GameMode.Paused && !HasPauseReasons && !IsQuizVisible && !IsGameOver)
            {
                Resume("restore");
            }
        }

        _lastTickMs = _clock.ElapsedMilliseconds;

        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);

        if (!_timerHooked)
        {
            _timerHooked = true;

            _timer.Tick += async (_, __) =>
            {
                try
                {
                    var now = _clock.ElapsedMilliseconds;
                    var dt = (int)Math.Clamp(now - _lastTickMs, 0, 100);
                    _lastTickMs = now;

                    await TickAsync(dt);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("TICK EXCEPTION: " + ex);
                    Pause("system");
                }
            };
        }

        _audio.ApplySettings(_settings.SfxEnabled, _settings.MusicEnabled);

        _timer.Start();
    }

    private void UpdateLevelBackground()
    {
        string file = State.Level switch
        {
            <= 5 => "lv_1_5.png",
            <= 10 => "lv_6_10.png",
            <= 15 => "lv_11_15.png",
            <= 20 => "lv_16_20.png",
            _ => "lv_20_plus.png",
        };

        LevelBackgroundImage = ImageSource.FromFile(file);
    }

    // Step 9.10: reasoned pause/resume
    public void Pause(string reason = "user")
    {
        if (Mode != GameMode.Playing) { AddPauseReason(reason); return; }

        AddPauseReason(reason);
        Mode = GameMode.Paused;
        _softDrop = false;
        Redraw();
    }

    public void Resume(string reason = "user")
    {
        if (Mode != GameMode.Paused) { RemovePauseReason(reason); return; }

        RemovePauseReason(reason);
        if (HasPauseReasons) return;

        Mode = GameMode.Playing;
        _fallAccS = 0;
        Redraw();
    }

    public void TogglePause()
    {
        if (Mode == GameMode.Playing) Pause("user");
        else if (Mode == GameMode.Paused) Resume("user");
    }

    public void Restart()
    {
        State.ClearBoard();
        Combo.Reset();

        State.CurrentPiece = CreateRandomPiece();
        State.NextPiece = CreateRandomPiece();

        _fallAccS = 0;
        _softDrop = false;
        _resolving = false;
        IsNewHighScore = false;
        _isSaveDisabled = true;
        _runStartedAtUtc = DateTimeOffset.UtcNow;


    // Reset Gravity/word cadance
    State.Score = 0;
        State.Level = 1;

        State.GravityIntervalMs = 600;
        State.WordsFoundCount = 0;

        State.FoundWords.Clear();
        State.RemovedWords.Clear();
        State.WordsFoundCount = 0;
        State.NoRepeatsActive = false;
        State.HoldUsed = false;

        // reset moments
        LockMoment = 0;
        ClearMoment = 0;
        BigClearMoment = 0;
        LevelUpMoment = 0;
        QuizCorrectMoment = 0;
        QuizWrongMoment = 0;

        // Clear flash
        _flashCells.Clear();
        _flashRemainingMs = 0;

        // Clear quiz UI state
        IsQuizVisible = false;
        QuizChoices.Clear();
        QuizWord = "";
        _correctChoice = "";

        // reset pause reasons
        lock (_pauseReasonLock) _pauseReasons.Clear();

        Mode = GameMode.Playing;

        RecentWords.Clear();

        // seed HUD cache so first UpdateHudIfChanged works cleanly
        _lastScore = Score;
        _lastLevel = Level;
        _lastMinLen = MinLen;
        _lastNoRepeats = NoRepeats;

        UpdateLevelBackground();
        Redraw();
        UpdateHudIfChanged();
        MarkSessionDirty();

        // fresh run should clear any existing resumable session
        _ = _sessionStore.ClearAsync();
    }

    public void SetSoftDrop(bool enabled)
    {
        _softDrop = (Mode == GameMode.Playing) && enabled;
    }

    public void MoveLeft()
    {
        if (!CanPlayInput) return;
        if (State.CurrentPiece?.Move(State, dx: -1) == true)
            Redraw();
    }

    public void MoveRight()
    {
        if (!CanPlayInput) return;
        if (State.CurrentPiece?.Move(State, dx: 1) == true)
            Redraw();
    }

    public void Rotate()
    {
        if (!CanPlayInput) return;
        if (State.CurrentPiece?.TryRotate(State) == true)
            Redraw();
    }

    public async void HardDrop()
    {
        if (!CanPlayInput) return;
        if (State.CurrentPiece is null) return;

        var dropped = State.CurrentPiece.HardDrop(State);
        if (dropped > 0) Redraw();

        await LockAndResolveAsync();

        Redraw();
        UpdateHudIfChanged();
    }

    public void HoldSwap()
    {
        if (!CanPlayInput) return;
        if (State.HoldUsed) return;
        if (State.CurrentPiece is null || State.NextPiece is null) return;

        if (State.HeldPiece is null)
        {
            State.HeldPiece = State.CurrentPiece;
            State.HeldPiece.ResetToSpawn();
            State.CurrentPiece = State.NextPiece;
            State.CurrentPiece.ResetToSpawn();
            State.NextPiece = CreateRandomPiece();
        }
        else
        {
            var temp = State.HeldPiece;
            State.HeldPiece = State.CurrentPiece;
            State.HeldPiece.ResetToSpawn();
            State.CurrentPiece = temp;
            State.CurrentPiece.ResetToSpawn();
        }

        State.HoldUsed = true;

        if (!State.CurrentPiece.CanMove(State, 0, 0))
            _ = HandleGameOverAsync();

        Redraw();
        MarkSessionDirty();
    }

    // -----------------------------
    // Save / Restore
    // -----------------------------

    public async Task SaveSessionAsync()
    {
        if (!_initialized) return;
        if (_isApplyingRestoredState) return;

        bool saved = false;
        GameSessionDto? dto = null;

        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Don’t persist a finished game. (Avoid restoring into GameOver unless you want that UX.)
            if (Mode == GameMode.GameOver)
            {
                await _sessionStore.ClearAsync().ConfigureAwait(false);
                return;
            }

            // If something is in-flight (resolving), skip this cycle to avoid partial snapshots.
            if (_resolving)
                return;

            dto = BuildSessionDto();
            await _sessionStore.SaveAsync(dto).ConfigureAwait(false);
            saved = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            // Saving is best-effort
        }
        finally
        {
            if (saved)
            {
                _lastSavedAt = dto?.SavedAt ?? DateTimeOffset.UtcNow;
                _isSessionDirty = false;
            }

            _sessionGate.Release();
        }
    }

    public async Task<bool> TryRestoreSessionAsync()
    {
        if (_restoreAttempted) return false;
        _restoreAttempted = true;

        await _sessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var dto = await _sessionStore.LoadAsync().ConfigureAwait(false);
            if (dto is null) return false;

            if (dto.Version != GameSessionDto.CurrentVersion)
            {
                await _sessionStore.ClearAsync().ConfigureAwait(false);
                return false;
            }

            var currentUser = (Preferences.Get("username", "") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(currentUser) ||
                !string.Equals(dto.Username ?? "", currentUser, StringComparison.OrdinalIgnoreCase))
            {
                await _sessionStore.ClearAsync().ConfigureAwait(false);
                return false;
            }

            var age = DateTimeOffset.UtcNow - dto.SavedAt;
            if (age < TimeSpan.Zero || age > TimeSpan.FromMinutes(10))
            {
                await _sessionStore.ClearAsync().ConfigureAwait(false);
                return false;
            }

            _isApplyingRestoredState = true;
            try
            {
                return ApplySessionDto(dto);
            }
            finally
            {
                _isApplyingRestoredState = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            return false;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private GameSessionDto BuildSessionDto()
    {
        // board as rows (fixed width)
        var rows = new string[GameConstants.Rows];
        for (int y = 0; y < GameConstants.Rows; y++)
        {
            var chars = new char[GameConstants.Cols];
            for (int x = 0; x < GameConstants.Cols; x++)
            {
                var c = State.Board[y, x];
                chars[x] = (c == '\0') ? '.' : c;
            }
            rows[y] = new string(chars);
        }

        return new GameSessionDto
        {
            Version = GameSessionDto.CurrentVersion,
            SavedAt = DateTimeOffset.UtcNow,

            Score = State.Score,
            Level = State.Level,
            GravityIntervalMs = State.GravityIntervalMs,
            WordsFoundCount = State.WordsFoundCount,

            HoldUsed = State.HoldUsed,

            BoardRows = rows,

            // Preserve no-repeats / cadence
            FoundWords = State.FoundWords.ToList(),
            RemovedWords = State.RemovedWords.ToList(),

            Current = SnapshotPiece(State.CurrentPiece),
            Next = SnapshotPiece(State.NextPiece),
            Hold = SnapshotPiece(State.HeldPiece),
            Username = (Preferences.Get("username", "") ?? "").Trim(),
        };
    }

    private bool ApplySessionDto(GameSessionDto dto)
    {
        // Basic validation
        if (dto.BoardRows is null || dto.BoardRows.Length != GameConstants.Rows)
            return false;

        if (dto.BoardRows.Any(r => r is null || r.Length != GameConstants.Cols))
            return false;

        // Reset transient systems first
        Combo.Reset();
        _fallAccS = 0;
        _softDrop = false;
        _resolving = false;

        // Clear flash / quiz UI
        _flashCells.Clear();
        _flashRemainingMs = 0;

        IsQuizVisible = false;
        QuizChoices.Clear();
        QuizWord = "";
        _correctChoice = "";

        // Apply board
        for (int y = 0; y < GameConstants.Rows; y++)
        {
            var row = dto.BoardRows[y];
            for (int x = 0; x < GameConstants.Cols; x++)
            {
                var c = row[x];
                State.Board[y, x] = (c == '.') ? '\0' : c;
            }
        }

        // Apply core progression
        State.Score = dto.Score;
        State.Level = Math.Max(1, dto.Level);
        State.GravityIntervalMs = Math.Max(60, dto.GravityIntervalMs);
        State.WordsFoundCount = Math.Max(0, dto.WordsFoundCount);

        State.HoldUsed = dto.HoldUsed;

        // Preserve no-repeats / cadence
        State.FoundWords.Clear();
        foreach (var w in dto.FoundWords ?? new List<string>())
            State.FoundWords.Add(w);

        State.RemovedWords.Clear();
        foreach (var w in dto.RemovedWords ?? new List<string>())
            State.RemovedWords.Add(w);

        // Restore pieces
        State.CurrentPiece = RestorePiece(dto.Current);
        State.NextPiece = RestorePiece(dto.Next);
        State.HeldPiece = RestorePiece(dto.Hold);

        if (State.CurrentPiece is null || State.NextPiece is null)
            return false;

        // After restore, we must ensure the current piece is in a valid place.
        // If it collides, bail (session likely stale/corrupt).
        if (!State.CurrentPiece.CanMove(State, 0, 0))
            return false;

        // Reset moments (optional: you can persist if you want)
        LockMoment = 0;
        ClearMoment = 0;
        BigClearMoment = 0;
        LevelUpMoment = 0;
        QuizCorrectMoment = 0;
        DateTimeOffset _runStartedAtUtc = DateTimeOffset.UtcNow;

    QuizWrongMoment = 0;

        // After restore: start paused, but do NOT add a reason.
        // (Otherwise the game can never auto-resume.)
        lock (_pauseReasonLock) _pauseReasons.Clear();
        Mode = GameMode.Paused;

        // seed HUD cache
        _lastScore = Score;
        _lastLevel = Level;
        _lastMinLen = MinLen;
        _lastNoRepeats = NoRepeats;

        UpdateLevelBackground();

        return true;
    }

    private static PieceSnapshotDto? SnapshotPiece(Piece? piece)
    {
        if (piece is null) return null;

        // Capture absolute current cells & letters, but store as offsets relative to min cell
        int minX = piece.Cells.Min(c => c.X);
        int minY = piece.Cells.Min(c => c.Y);

        var offsets = new List<GridCell>(piece.Cells.Count);
        for (int i = 0; i < piece.Cells.Count; i++)
        {
            var cell = piece.Cells[i];
            offsets.Add(new GridCell(cell.X - minX, cell.Y - minY));
        }

        return new PieceSnapshotDto
        {
            MinX = minX,
            MinY = minY,
            Offsets = offsets,
            Letters = new List<char>(piece.Letters)
        };
    }

    private Piece? RestorePiece(PieceSnapshotDto? snap)
    {
        if (snap is null) return null;
        if (snap.Offsets is null || snap.Offsets.Count == 0) return null;
        if (snap.Letters is null || snap.Letters.Count == 0) return null;

        // Create a piece from offsets (shape in local space)
        var shape = snap.Offsets.Select(o => new GridCell(o.X, o.Y)).ToList();
        var letters = new List<char>(snap.Letters);

        var piece = new Piece(shape, letters);

        // Try to place at saved position by moving from spawn
        piece.ResetToSpawn();

        // We move by aligning min cell position. This is conservative and uses existing collision logic.
        int curMinX = piece.Cells.Min(c => c.X);
        int curMinY = piece.Cells.Min(c => c.Y);

        int dx = snap.MinX - curMinX;
        int dy = snap.MinY - curMinY;

        if (!TryStepMove(piece, dx, 0)) return piece; // best effort (for next/hold this usually doesn’t matter)
        if (!TryStepMove(piece, 0, dy)) return piece;

        return piece;

        bool TryStepMove(Piece p, int dxTotal, int dyTotal)
        {
            int sx = Math.Sign(dxTotal);
            for (int i = 0; i < Math.Abs(dxTotal); i++)
            {
                if (!p.Move(State, dx: sx, dy: 0)) return false;
            }

            int sy = Math.Sign(dyTotal);
            for (int i = 0; i < Math.Abs(dyTotal); i++)
            {
                if (!p.Move(State, dx: 0, dy: sy)) return false;
            }

            return true;
        }
    }

    // -----------------------------
    // Game loop
    // -----------------------------

    private async Task TickAsync(int dtMs)
    {
        if (Mode != GameMode.Playing || _resolving)
        {
            if (_flashRemainingMs > 0)
            {
                _flashRemainingMs -= dtMs;
                if (_flashRemainingMs <= 0)
                {
                    _flashRemainingMs = 0;
                    _flashCells.Clear();
                }
                Redraw();
            }
            return;
        }

        if (State.CurrentPiece is null || State.NextPiece is null) return;

        bool visualsChanged = false;

        Combo.Update(dtMs);
        if (State.GravityIntervalMs <= 0)
            State.GravityIntervalMs = 600;

        _fallAccS += (dtMs / 1000.0) * (_softDrop ? 5.0 : 1.0);
        var intervalS = State.GravityIntervalMs / 1000.0;
        if (intervalS <= 0)
            return;

        while (_fallAccS >= intervalS)
        {
            _fallAccS -= intervalS;

            if (!State.CurrentPiece.Move(State, dy: 1))
            {
                await LockAndResolveAsync();
                visualsChanged = true;
                break;
            }

            visualsChanged = true;
        }

        if (_flashRemainingMs > 0)
        {
            _flashRemainingMs -= dtMs;
            visualsChanged = true;

            if (_flashRemainingMs <= 0)
            {
                _flashRemainingMs = 0;
                _flashCells.Clear();
            }
        }

        if (visualsChanged)
            Redraw();

        UpdateHudIfChanged();
    }

    private async Task LockAndResolveAsync()
    {
        if (State.CurrentPiece is null || State.NextPiece is null) return;

        _resolving = true;
        try
        {
            var lastPositions = State.CurrentPiece.Cells.ToList();
            State.CurrentPiece.LockToBoard(State);

            LockMoment++;

            while (true)
            {
                double effective = Combo.EffectiveMultiplier(State.ScoreMultiplier);
                var result = await CheckAndRemoveWords(lastPositions, effective);

                if (!result.WordsFound) break;

                StartClearFlash(result.ClearedPositions);
                Combo.OnClear();
            }

            State.CurrentPiece = State.NextPiece;
            State.CurrentPiece.ResetToSpawn();
            State.NextPiece = CreateRandomPiece();
            State.HoldUsed = false;
            _fallAccS = 0;

            if (!State.CurrentPiece.CanMove(State, 0, 0))
                await HandleGameOverAsync();
        }
        finally
        {
            _resolving = false;
            MarkSessionDirty();
        }
    }

    private async Task HandleGameOverAsync()
    {
        StopAutoSave();

        if (Mode == GameMode.GameOver) return;

        Mode = GameMode.GameOver;
        _softDrop = false;

        lock (_pauseReasonLock) _pauseReasons.Clear();

        IsNewHighScore = false;
        if (State.Score > _settings.BestScore)
        {
            _settings.BestScore = State.Score;
            IsNewHighScore = true;
            OnPropertyChanged(nameof(BestScore));
        }

        // don’t restore into a dead run
        _ = _sessionStore.ClearAsync();

        // Publish result for GameOverPage
        var endedAt = DateTimeOffset.UtcNow;
        var duration = endedAt - _runStartedAtUtc;
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

        _resultBus.Set(new GameResult
        {
            Score = State.Score,
            Level = State.Level,

            // You don't currently track "lines", so use something meaningful for now.
            // You can replace this later when you add a true "lines cleared" stat.
            Lines = State.RemovedWords.Count,

            // Unique words found (or use RemovedWords.Count if you prefer)
            WordsCleared = State.FoundWords.Count,

            Duration = duration,
            EndedAt = endedAt,

            // Optional for later filtering
            ModeKey = "classic",
            OptionsHash = ""
        });

        Redraw();

        // Navigate to GameOver page (Shell route "gameover" must be registered)
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await Shell.Current.GoToAsync("//gameover");
        });
        /*
        if (RequestGameOver is not null)
            await RequestGameOver();
        */
    }

    private void StartClearFlash(IEnumerable<GridCell> cells)
    {
        _flashCells = cells.ToList();
        _flashRemainingMs = ClearFlashMs;
    }

    private void UpdateHudIfChanged()
    {
        int score = Score;
        int level = Level;
        int minLen = MinLen;
        bool noRepeats = NoRepeats;

        if (score != _lastScore) { _lastScore = score; OnPropertyChanged(nameof(Score)); }
        if (level != _lastLevel) { _lastLevel = level; OnPropertyChanged(nameof(Level)); }
        if (minLen != _lastMinLen) { _lastMinLen = minLen; OnPropertyChanged(nameof(MinLen)); }
        if (noRepeats != _lastNoRepeats) { _lastNoRepeats = noRepeats; OnPropertyChanged(nameof(NoRepeats)); }

        OnPropertyChanged(nameof(ComboMultiplier));
        OnPropertyChanged(nameof(IsComboActive));
    }

    private RenderPiece? BuildPreview(Piece? piece, Color fill)
    {
        if (piece is null) return null;

        int minX = piece.ShapeOffsets.Min(c => c.X);
        int minY = piece.ShapeOffsets.Min(c => c.Y);

        var blocks = new List<(int Col, int Row, char Letter)>(piece.ShapeOffsets.Count);
        for (int i = 0; i < piece.ShapeOffsets.Count; i++)
        {
            var o = piece.ShapeOffsets[i];
            var letter = i < piece.Letters.Count ? piece.Letters[i] : '?';
            blocks.Add((o.X - minX, o.Y - minY, letter));
        }

        return new RenderPiece { Fill = fill, Blocks = blocks };
    }

    private void UpdateRenderState()
    {
        var boardCopy = (char[,])State.Board.Clone();

        RenderPiece? active = null;
        RenderPiece? ghost = null;

        var p = State.CurrentPiece;
        if (p is not null)
        {
            var blocks = new List<(int Col, int Row, char Letter)>(p.Cells.Count);
            for (int i = 0; i < p.Cells.Count; i++)
            {
                var cell = p.Cells[i];
                var letter = i < p.Letters.Count ? p.Letters[i] : '?';
                blocks.Add((cell.X, cell.Y, letter));
            }

            active = new RenderPiece
            {
                Fill = Colors.OrangeRed,
                Blocks = blocks
            };

            int ghostDy = 0;
            while (p.CanMove(State, 0, ghostDy + 1))
                ghostDy++;

            if (ghostDy > 0)
            {
                var gblocks = new List<(int Col, int Row, char Letter)>(p.Cells.Count);
                for (int i = 0; i < p.Cells.Count; i++)
                {
                    var cell = p.Cells[i];
                    var letter = i < p.Letters.Count ? p.Letters[i] : '?';
                    gblocks.Add((cell.X, cell.Y + ghostDy, letter));
                }

                ghost = new RenderPiece
                {
                    Fill = Colors.White.WithAlpha(0.18f),
                    Blocks = gblocks
                };
            }
        }

        var flash = _flashCells.Select(c => (c.X, c.Y)).ToList();

        CurrentRenderState = new GameRenderState
        {
            Columns = GameConstants.Cols,
            Rows = GameConstants.Rows,
            BoardLetters = boardCopy,
            ActivePiece = active,
            GhostPiece = ghost,
            FlashCells = flash
        };

        NextPieceRender = BuildPreview(State.NextPiece, Colors.MediumPurple);
        HoldPieceRender = BuildPreview(State.HeldPiece, Colors.Teal);
        OnPropertyChanged(nameof(NextPieceRender));
        OnPropertyChanged(nameof(HoldPieceRender));
    }

    private void Redraw()
    {
        UpdateRenderState();
        RequestRedraw?.Invoke();
    }

    private Piece CreateRandomPiece()
    {
        var shapes = new List<List<GridCell>>
        {
            new() { new(0,0), new(1,0), new(2,0), new(3,0) },
            new() { new(0,0), new(1,0), new(0,1), new(1,1) },
            new() { new(0,0), new(1,0), new(2,0), new(2,1) },
            new() { new(0,1), new(1,1), new(2,1), new(2,0) },
            new() { new(0,0), new(1,0), new(1,1), new(2,1) },
            new() { new(0,1), new(1,1), new(1,0), new(2,0) },
        };

        var weights = new Dictionary<char, int>
        {
            ['A'] = 8,
            ['E'] = 8,
            ['I'] = 8,
            ['O'] = 8,
            ['U'] = 8,
            ['B'] = 2,
            ['C'] = 2,
            ['D'] = 3,
            ['F'] = 1,
            ['G'] = 2,
            ['H'] = 2,
            ['J'] = 1,
            ['K'] = 1,
            ['L'] = 3,
            ['M'] = 2,
            ['N'] = 4,
            ['P'] = 2,
            ['Q'] = 1,
            ['R'] = 4,
            ['S'] = 4,
            ['T'] = 4,
            ['V'] = 1,
            ['W'] = 2,
            ['X'] = 1,
            ['Y'] = 2,
            ['Z'] = 1
        };

        char GenerateLetter()
        {
            var ls = weights.Keys.ToList();
            var ws = weights.Values.ToList();
            return _rng.WeightedChoice(ls, ws);
        }

        var shape = _rng.Choice(shapes);
        var letters = new List<char>();
        bool consonantIncluded = false;

        foreach (var _ in shape)
        {
            var L = GenerateLetter();
            if (!"AEIOU".Contains(L)) consonantIncluded = true;
            letters.Add(L);
        }

        if (!consonantIncluded)
        {
            var consonants = weights.Keys.Where(c => !"AEIOU".Contains(c)).ToList();
            letters[_rng.Next(0, 4)] = _rng.Choice(consonants);
        }

        return new Piece(shape, letters);
    }

    private async Task<(bool WordsFound, List<GridCell> ClearedPositions)> CheckAndRemoveWords(
        List<GridCell> lastBlockPositions,
        double effectiveMultiplier)
    {
        bool wordsFound = false;
        int minLen = GameConstants.MinWordLength(State.Level);
        State.NoRepeatsActive = (minLen >= 5);

        var toRemove = new List<(string Word, List<GridCell> Positions)>();

        // Horizontal
        for (int y = 0; y < GameConstants.Rows; y++)
        {
            for (int sx = 0; sx < GameConstants.Cols; sx++)
            {
                for (int ex = sx + minLen; ex <= GameConstants.Cols; ex++)
                {
                    int len = ex - sx;
                    bool anyEmpty = false;
                    var chars = new char[len];

                    for (int i = 0; i < len; i++)
                    {
                        var c = State.Board[y, sx + i];
                        if (c == '\0') { anyEmpty = true; break; }
                        chars[i] = c;
                    }

                    if (anyEmpty) continue;

                    var word = new string(chars);
                    var wl = word.ToLowerInvariant();

                    if (!_commonWords.Contains(wl)) continue;
                    if (State.NoRepeatsActive && State.FoundWords.Contains(wl)) continue;

                    var positions = Enumerable.Range(sx, len).Select(x => new GridCell(x, y)).ToList();
                    toRemove.Add((word, positions));
                }
            }
        }

        // Vertical
        for (int x = 0; x < GameConstants.Cols; x++)
        {
            for (int sy = 0; sy < GameConstants.Rows; sy++)
            {
                for (int ey = sy + minLen; ey <= GameConstants.Rows; ey++)
                {
                    int len = ey - sy;
                    bool anyEmpty = false;
                    var chars = new char[len];

                    for (int i = 0; i < len; i++)
                    {
                        var c = State.Board[sy + i, x];
                        if (c == '\0') { anyEmpty = true; break; }
                        chars[i] = c;
                    }

                    if (anyEmpty) continue;

                    var word = new string(chars);
                    var wl = word.ToLowerInvariant();

                    if (!_commonWords.Contains(wl)) continue;
                    if (State.NoRepeatsActive && State.FoundWords.Contains(wl)) continue;

                    var positions = Enumerable.Range(sy, len).Select(y => new GridCell(x, y)).ToList();
                    toRemove.Add((word, positions));
                }
            }
        }

        // Prefer longer words
        toRemove.Sort((a, b) => b.Word.Length.CompareTo(a.Word.Length));

        var cleared = new HashSet<GridCell>();
        var removedPositionsAll = new List<GridCell>();

        foreach (var (word, positions) in toRemove)
        {
            if (positions.Any(p => cleared.Contains(p))) continue;

            wordsFound = true;
            string wl = word.ToLowerInvariant();

            State.Score += (int)(word.Length * 10 * State.Level * effectiveMultiplier);
            State.RemovedWords.Add(word);
            State.FoundWords.Add(wl);
            State.WordsFoundCount += 1;

            RecentWords.Insert(0, word);
            while (RecentWords.Count > 12) RecentWords.RemoveAt(RecentWords.Count - 1);

            // Quiz every 5 words
            if (State.RemovedWords.Count % 5 == 0)
                _ = ShowDefinitionQuizAsync(word);

            foreach (var p in positions)
            {
                State.Board[p.Y, p.X] = '\0';
                cleared.Add(p);
                removedPositionsAll.Add(p);
            }
        }

        if (wordsFound)
        {
            ClearMoment++;

            if (removedPositionsAll.Count >= 4)
                BigClearMoment++;

            // Gravity collapse by column
            for (int x = 0; x < GameConstants.Cols; x++)
            {
                var stack = new List<char>();
                for (int y = 0; y < GameConstants.Rows; y++)
                {
                    var c = State.Board[y, x];
                    if (c != '\0') stack.Add(c);
                }

                for (int y = GameConstants.Rows - 1; y >= 0; y--)
                {
                    if (stack.Count > 0)
                    {
                        State.Board[y, x] = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                    }
                    else State.Board[y, x] = '\0';
                }
            }

            if (State.WordsFoundCount >= 10)
            {
                State.Level += 1;

                UpdateLevelBackground();

                LevelUpMoment++;

                State.WordsFoundCount = 0;
                State.GravityIntervalMs = Math.Max(120, (int)(State.GravityIntervalMs * 0.90));
            }
        }

        UpdateHudIfChanged();
        return (wordsFound, removedPositionsAll);
    }

    private async Task ShowDefinitionQuizAsync(string word)
    {
        if (IsQuizVisible) return;

        AddPauseReason("quiz");
        Mode = GameMode.Quiz;
        _softDrop = false;

        try
        {
            QuizWord = word.ToUpperInvariant();
            QuizChoices.Clear();

            var defs = await _defs.GetDefinitionsAsync(word.ToLowerInvariant(), _commonWords, _filter, _rng);
            string correct = defs.FirstOrDefault() ?? "No definition";
            _correctChoice = correct;

            var decoys = new List<string>();
            var commonList = _commonWords.ToList();

            for (int i = 0; i < 12 && decoys.Count < 3 && commonList.Count > 0; i++)
            {
                var w = _rng.Choice(commonList);
                if (await _filter.IsBannedWordAsync(w)) continue;

                var d = await _defs.GetDefinitionsAsync(w, _commonWords, _filter, _rng);
                if (d.Count > 0) decoys.Add(d[0]);
            }

            while (decoys.Count < 3) decoys.Add("—");

            var choices = new List<string> { correct };
            choices.AddRange(decoys);

            for (int i = choices.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(0, i + 1);
                (choices[i], choices[j]) = (choices[j], choices[i]);
            }

            foreach (var c in choices) QuizChoices.Add(c);

            IsQuizVisible = true;
            Redraw();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            IsQuizVisible = false;

            RemovePauseReason("quiz");

            if (Mode == GameMode.Quiz)
            {
                if (!HasPauseReasons) Mode = GameMode.Playing;
                else Mode = GameMode.Paused;
            }

            Redraw();
        }
    }

    public void QuizPick(string choice)
    {
        if (!IsQuizVisible) return;

        if (choice == _correctChoice)
        {
            State.Score += 50;
            QuizCorrectMoment++;
        }
        else
        {
            MoveLettersUp();
            AddRandomRow();
            QuizWrongMoment++;
        }

        IsQuizVisible = false;
        RemovePauseReason("quiz");

        if (Mode == GameMode.Quiz)
        {
            if (!HasPauseReasons) Mode = GameMode.Playing;
            else Mode = GameMode.Paused;
        }

        UpdateHudIfChanged();
        Redraw();
        MarkSessionDirty();
    }

    public void QuizSkip()
    {
        if (!IsQuizVisible) return;

        IsQuizVisible = false;
        RemovePauseReason("quiz");

        if (Mode == GameMode.Quiz)
        {
            if (!HasPauseReasons) Mode = GameMode.Playing;
            else Mode = GameMode.Paused;
        }

        Redraw();
        MarkSessionDirty();
    }

    private void MoveLettersUp()
    {
        for (int y = 1; y < GameConstants.Rows; y++)
            for (int x = 0; x < GameConstants.Cols; x++)
                State.Board[y - 1, x] = State.Board[y, x];

        for (int x = 0; x < GameConstants.Cols; x++)
            State.Board[GameConstants.Rows - 1, x] = '\0';
    }

    private void AddRandomRow()
    {
        var p = CreateRandomPiece();
        for (int x = 0; x < GameConstants.Cols; x++)
            State.Board[GameConstants.Rows - 1, x] = p.Letters[_rng.Next(0, p.Letters.Count)];
    }

    public bool SfxEnabled
    {
        get => _settings.SfxEnabled;
        set
        {
            if (_settings.SfxEnabled == value) return;
            _settings.SfxEnabled = value;
            _audio.ApplySettings(_settings.SfxEnabled, _settings.MusicEnabled);
            OnPropertyChanged();
        }
    }

    public bool MusicEnabled
    {
        get => _settings.MusicEnabled;
        set
        {
            if (_settings.MusicEnabled == value) return;
            _settings.MusicEnabled = value;
            _audio.ApplySettings(_settings.SfxEnabled, _settings.MusicEnabled);
            OnPropertyChanged();
        }
    }

    public bool IsNewHighScore
    {
        get => _isNewHighScore;
        private set { _isNewHighScore = value; OnPropertyChanged(); }
    }
    private bool _isNewHighScore;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}