namespace LettriisMaui;

public static class AppEvents
{
    public static event Action? AppSleeping;

    internal static void RaiseAppSleeping()
        => AppSleeping?.Invoke();
}