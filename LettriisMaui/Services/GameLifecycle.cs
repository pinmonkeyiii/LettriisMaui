namespace LettriisMaui.Services;

public static class GameLifecycle
{
    public static event Action<string>? PauseRequested;
    public static event Action<string>? ResumeRequested;

    public static void RequestPause(string reason) => PauseRequested?.Invoke(reason);
    public static void RequestResume(string reason) => ResumeRequested?.Invoke(reason);
}