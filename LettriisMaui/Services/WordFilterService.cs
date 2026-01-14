
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LettriisMaui.Services;

public sealed class WordFilterService
{
    private static readonly Dictionary<char, char> LeetMap = new()
    {
        ['@'] = 'a', ['4'] = 'a',
        ['0'] = 'o',
        ['1'] = 'i', ['!'] = 'i',
        ['$'] = 's', ['5'] = 's',
        ['7'] = 't',
        ['3'] = 'e',
    };

    private readonly BannedWordsService _banned;

    public WordFilterService(BannedWordsService banned) => _banned = banned;

    public async Task<bool> IsBannedWordAsync(string word)
    {
        var banned = await _banned.GetBannedAsync();
        var w = Normalize(word);
        return banned.Contains(w);
    }

    public async Task<bool> ContainsBannedSubstringAsync(string text)
    {
        var banned = await _banned.GetBannedAsync();
        var t = Normalize(text);
        foreach (var bw in banned)
        {
            if (Regex.IsMatch(t, $@"\b{Regex.Escape(bw)}\b"))
                return true;
        }
        return false;
    }

    public async Task<List<string>> FilterDefinitionsAsync(IEnumerable<string> defs)
    {
        var safe = new List<string>();
        foreach (var d in defs)
            if (!await ContainsBannedSubstringAsync(d)) safe.Add(d);
        return safe;
    }

    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string decomp = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomp.Length);

        foreach (var ch in decomp)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;

            var lower = char.ToLowerInvariant(ch);
            if (LeetMap.TryGetValue(lower, out var mapped)) lower = mapped;

            if (char.IsLetterOrDigit(lower) || lower == '_' || char.IsWhiteSpace(lower))
                sb.Append(lower);
            else
                sb.Append(' ');
        }

        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
