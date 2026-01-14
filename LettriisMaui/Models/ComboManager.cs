
namespace LettriisMaui.Models;

public sealed class ComboManager
{
    public int DecayMs { get; }
    public double Growth { get; }
    public double StartMult { get; }
    public double MaxMult { get; }

    public double ComboMult { get; private set; }
    public int ComboStep { get; private set; }
    public int SinceLastClearMs { get; private set; }
    public int FlashMs { get; private set; }

    public ComboManager(int decayMs = 9000, double growth = 0.5, double startMult = 1.0, double maxMult = 4.0)
    {
        DecayMs = decayMs;
        Growth = growth;
        StartMult = startMult;
        MaxMult = maxMult;
        Reset();
    }

    public void Reset()
    {
        ComboMult = StartMult;
        ComboStep = 0;
        SinceLastClearMs = 0;
        FlashMs = 0;
    }

    public void OnClear()
    {
        ComboStep += 1;
        ComboMult = Math.Min(StartMult + Growth * (ComboStep - 1), MaxMult);
        SinceLastClearMs = 0;
        FlashMs = 300;
    }

    public void Update(int dtMs)
    {
        SinceLastClearMs += dtMs;
        if (SinceLastClearMs > DecayMs && ComboStep > 0)
        {
            ComboStep -= 1;
            ComboMult = Math.Max(StartMult + Growth * Math.Max(0, ComboStep - 1), StartMult);
            SinceLastClearMs = 0;
            FlashMs = 240;
        }
        if (FlashMs > 0) FlashMs = Math.Max(0, FlashMs - dtMs);
    }

    public double EffectiveMultiplier(double baseMult = 1.0) => baseMult * ComboMult;
    public bool IsActive => ComboStep > 0;
}
