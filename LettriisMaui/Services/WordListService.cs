
namespace LettriisMaui.Services;

public sealed class WordListService
{
    private HashSet<string>? _words;

    public async Task<HashSet<string>> GetCommonWordsAsync()
    {
        if (_words is not null) return _words;

        try
        {
            using var s = await FileSystem.OpenAppPackageFileAsync("scrabble_dictionary.txt");
            using var r = new StreamReader(s);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!r.EndOfStream)
            {
                var line = (await r.ReadLineAsync())?.Trim();
                if (!string.IsNullOrWhiteSpace(line))
                    set.Add(line.ToLowerInvariant());
            }
            _words = set;
        }
        catch
        {
            _words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return _words;
    }
}
