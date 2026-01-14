using System.Text.Json;
using LettriisMaui.Models;

namespace LettriisMaui.Services.HighScores;

public sealed class JsonHighScoreStore : IHighScoreStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public JsonHighScoreStore(string? fileName = null)
    {
        _path = Path.Combine(FileSystem.AppDataDirectory, fileName ?? "highscores.json");
    }

    public async Task<IReadOnlyList<HighScoreEntry>> GetAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await ReadInternalAsync(ct);
        }
        finally { _gate.Release(); }
    }

    public async Task SubmitAsync(HighScoreEntry entry, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var list = (await ReadInternalAsync(ct)).ToList();

            list.Add(entry);

            // Keep it simple: global Top 50.
            // (You can later bucket/filter by ModeKey/OptionsHash without changing storage format.)
            list = list
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Level)
                .ThenBy(x => x.Duration) // shorter is better as tie-breaker
                .Take(50)
                .ToList();

            await WriteAtomicAsync(list, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<HighScoreEntry>> ReadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return Array.Empty<HighScoreEntry>();

        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<List<HighScoreEntry>>(stream, _json, ct)
               ?? new List<HighScoreEntry>();
    }

    private async Task WriteAtomicAsync(List<HighScoreEntry> list, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        await using (var stream = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(stream, list, _json, ct);
            await stream.FlushAsync(ct);
        }

        if (File.Exists(_path))
            File.Delete(_path);

        File.Move(tmp, _path);
    }
}