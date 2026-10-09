using System.Net;
using System.Text.RegularExpressions;

namespace CinePosto.Domain.Text;

/// <summary>Faithful port of the legacy Python title normalizer (parity first, see ADR 0002).</summary>
public static partial class TitleNormalizer
{
    /// <summary>Normalizes a scraped film title for display and comparison.</summary>
    public static string Normalize(string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        string t = WebUtility.HtmlDecode(title.Trim());
        t = t.Replace('’', '\'');
        t = t.Replace('‘', '\'');
        t = t.Replace('“', '"');
        t = t.Replace('”', '"');
        t = t.Replace('–', '-');
        t = t.Replace('—', '-');
        t = AmpersandFold().Replace(t, " e ");

        for (int i = 0; i < 3; i++)
        {
            t = TitleSuffixes().Replace(t, string.Empty).Trim();
        }

        t = RieditionSuffix().Replace(t, string.Empty).Trim();
        t = YearSuffix().Replace(t, string.Empty).Trim();
        t = CASuffix().Replace(t, string.Empty).Trim();
        t = AndWord().Replace(t, string.Empty).Trim();
        t = FranchisePrefixes().Replace(t, string.Empty).Trim();

        t = CollapseWhitespace().Replace(t, " ");

        t = t.Trim(' ', '.', ',', ';', ':', '!', '?', '-', '–', '—');

        if (t.Length > 0 && t[^1] is not ')' and not ']' and not '}')
        {
            t = t.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Trim(' ', '.', ',', ';', ':', '!', '?', '-', '–', '—');
        }

        return t;
    }

    /// <summary>Builds the lowercase comparison key for a film title.</summary>
    public static string Key(string title)
    {
        string normalized = Normalize(title).ToLowerInvariant();
        normalized = KeyStripNonWord().Replace(normalized, string.Empty);
        normalized = KeyStripWhitespace().Replace(normalized, string.Empty);
        return normalized;
    }

    // Original Python: re.sub(r"\s*&\s*", " e ", t)
    private static Regex AmpersandFold() => new(@"\s*&\s*");

    // Original Python: _TITLE_SUFFIXES = re.compile(r"\s*[-–]\s*(3D|IMAX|HFR|4DX|ScreenX|EPIC|Dolby Atmos|Dolby Cinema|VIP|Gold|OV|VOST|VO|VF|Versione Originale|Sottotitolato|Sub ITA|Live|Event|F\&S|F&S|Family|Kids|Matinée|Sera|Notte)$", re.IGNORECASE)
    [GeneratedRegex(@"\s*[-–]\s*(3D|IMAX|HFR|4DX|ScreenX|EPIC|Dolby Atmos|Dolby Cinema|VIP|Gold|OV|VOST|VO|VF|Versione Originale|Sottotitolato|Sub ITA|Live|Event|F\&S|F&S|Family|Kids|Matinée|Sera|Notte)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleSuffixes();

    // Original Python: _RIEDITION_SUFFIX = re.compile(r"\s+4K\s*\(RIED\.?\s*\d{4}\)\s*(C\.A\.?)?\s*$", re.IGNORECASE)
    [GeneratedRegex(@"\s+4K\s*\(RIED\.?\s*\d{4}\)\s*(C\.A\.?)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RieditionSuffix();

    // Original Python: _YEAR_SUFFIX = re.compile(r"\s*[\(\[]?\s*(?:19|20)\d{2}\s*[\)\]]?\s*$") (case-sensitive, no flags)
    [GeneratedRegex(@"\s*[\(\[]?\s*(?:19|20)\d{2}\s*[\)\]]?\s*$")]
    private static partial Regex YearSuffix();

    // Original Python: _C_A_SUFFIX = re.compile(r"\s+C\.A\.?\s*$", re.IGNORECASE)
    [GeneratedRegex(@"\s+C\.A\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CASuffix();

    // Original Python: _AND_WORD = re.compile(r"\bAND\b", re.IGNORECASE)
    [GeneratedRegex(@"\bAND\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AndWord();

    // Original Python: _FRANCHISE_PREFIXES = re.compile(r"^(?:STAR WARS:\s*|MARVEL(?:'S)?\s+|DC\s+|PIXAR\s+|Disney\s+|THE\s+|IL\s+|LA\s+)", re.IGNORECASE)
    [GeneratedRegex(@"^(?:STAR WARS:\s*|MARVEL(?:'S)?\s+|DC\s+|PIXAR\s+|Disney\s+|THE\s+|IL\s+|LA\s+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FranchisePrefixes();

    // Original Python: re.sub(r"\s+", " ", t)
    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespace();

    // Original Python (title_key): re.sub(r"[^\w\s]", "", normalized)
    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex KeyStripNonWord();

    // Original Python (title_key): re.sub(r"\s+", "", normalized)
    [GeneratedRegex(@"\s+")]
    private static partial Regex KeyStripWhitespace();
}
