
using System.Net.Http.Json;

namespace LettriisMaui.Services;

public sealed class DefinitionService
{
    private readonly HttpClient _http = new();

    public async Task<List<string>> GetDefinitionsAsync(string word, HashSet<string> commonWords, WordFilterService filter, RandomService rng)
    {
        var defs = new List<string>();

        try
        {
            var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(word)}";
            var data = await _http.GetFromJsonAsync<List<DictionaryApiEntry>>(url);
            if (data is not null && data.Count > 0)
                defs.AddRange(Flatten(data));
        }
        catch { }

        int attempts = 0;
        var commonList = commonWords.ToList();
        while (defs.Count < 4 && attempts < 8 && commonList.Count > 0)
        {
            attempts++;
            var rnd = rng.Choice(commonList);
            try
            {
                var url = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(rnd)}";
                var data = await _http.GetFromJsonAsync<List<DictionaryApiEntry>>(url);
                if (data is not null && data.Count > 0)
                    defs.AddRange(Flatten(data));
            }
            catch { }
        }

        var cleaned = defs
            .Select(d => (d ?? string.Empty).Replace("[", "").Replace("]", "").Replace("'", "").Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        // shuffle
        for (int i = cleaned.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (cleaned[i], cleaned[j]) = (cleaned[j], cleaned[i]);
        }

        return await filter.FilterDefinitionsAsync(cleaned);
    }

    private static IEnumerable<string> Flatten(List<DictionaryApiEntry> entries)
    {
        foreach (var e in entries)
        {
            if (e.Meanings is null) continue;
            foreach (var m in e.Meanings)
            {
                if (m.Definitions is null) continue;
                foreach (var d in m.Definitions)
                    if (!string.IsNullOrWhiteSpace(d.Definition))
                        yield return d.Definition!;
            }
        }
    }

    private sealed class DictionaryApiEntry { public List<Meaning>? Meanings { get; set; } }
    private sealed class Meaning { public List<Def>? Definitions { get; set; } }
    private sealed class Def { public string? Definition { get; set; } }
}
