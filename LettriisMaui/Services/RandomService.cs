
namespace LettriisMaui.Services;

public sealed class RandomService
{
    private readonly Random _rng = new();

    public T Choice<T>(IReadOnlyList<T> items) => items[_rng.Next(items.Count)];
    public int Next(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);

    public T WeightedChoice<T>(IReadOnlyList<T> items, IReadOnlyList<int> weights)
    {
        if (items.Count != weights.Count) throw new ArgumentException("items and weights length mismatch");
        int total = 0;
        for (int i = 0; i < weights.Count; i++) total += weights[i];
        int pick = _rng.Next(0, total);
        int acc = 0;
        for (int i = 0; i < items.Count; i++)
        {
            acc += weights[i];
            if (pick < acc) return items[i];
        }
        return items[^1];
    }
}
