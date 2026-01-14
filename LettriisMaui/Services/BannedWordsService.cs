
namespace LettriisMaui.Services;

public sealed class BannedWordsService
{
    private HashSet<string>? _banned;

    public async Task<HashSet<string>> GetBannedAsync()
    {
        if (_banned is not null) return _banned;

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var s = await FileSystem.OpenAppPackageFileAsync("banned_words.txt");
            using var r = new StreamReader(s);
            while (!r.EndOfStream)
            {
                var line = (await r.ReadLineAsync())?.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(line)) set.Add(line);
            }
        }
        catch { }
        _banned = set;
        return _banned;
    }
}
