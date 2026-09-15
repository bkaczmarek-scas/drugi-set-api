using System.Text;
using System.Text.RegularExpressions;

namespace DrugiSet.Api.Posts;

public static class SlugGenerator
{
    private static readonly Dictionary<char, string> PolishMap = new()
    {
        ['ą'] = "a", ['ć'] = "c", ['ę'] = "e", ['ł'] = "l", ['ń'] = "n",
        ['ó'] = "o", ['ś'] = "s", ['ź'] = "z", ['ż'] = "z",
        ['Ą'] = "a", ['Ć'] = "c", ['Ę'] = "e", ['Ł'] = "l", ['Ń'] = "n",
        ['Ó'] = "o", ['Ś'] = "s", ['Ź'] = "z", ['Ż'] = "z",
    };

    public static string Generate(string title)
    {
        var builder = new StringBuilder();
        foreach (var ch in title)
        {
            builder.Append(PolishMap.TryGetValue(ch, out var mapped) ? mapped : ch.ToString());
        }

        var lowered = builder.ToString().ToLowerInvariant();
        var withDashes = Regex.Replace(lowered, "[^a-z0-9]+", "-");
        var slug = withDashes.Trim('-');
        return string.IsNullOrEmpty(slug) ? "wpis" : slug;
    }

    public static string MakeUnique(string baseSlug, ISet<string> existingSlugs)
    {
        if (!existingSlugs.Contains(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        while (existingSlugs.Contains($"{baseSlug}-{suffix}"))
        {
            suffix++;
        }

        return $"{baseSlug}-{suffix}";
    }
}
