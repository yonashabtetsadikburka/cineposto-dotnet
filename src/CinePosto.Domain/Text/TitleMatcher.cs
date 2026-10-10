namespace CinePosto.Domain.Text;

/// <summary>Faithful port of the legacy Python title matching (parity first, see ADR 0002).</summary>
public static class TitleMatcher
{
    /// <summary>Returns true when both titles share the same comparison key.</summary>
    public static bool AreEqual(string a, string b)
    {
        return TitleNormalizer.Key(a).Equals(TitleNormalizer.Key(b), StringComparison.Ordinal);
    }

    /// <summary>Returns true when both titles match exactly, by substring, or within a small edit distance.</summary>
    public static bool IsFuzzyMatch(string a, string b)
    {
        string ka = TitleNormalizer.Key(a);
        string kb = TitleNormalizer.Key(b);
        if (ka.Equals(kb, StringComparison.Ordinal))
        {
            return true;
        }

        if (ka.Length >= 4 && kb.Length >= 4 &&
            (kb.Contains(ka, StringComparison.Ordinal) || ka.Contains(kb, StringComparison.Ordinal)))
        {
            return true;
        }

        // Legacy quirk: the threshold is asymmetric, it only uses the length of the first key.
        if (EditDistance.Levenshtein(ka, kb) <= Math.Max(2, ka.Length / 4))
        {
            return true;
        }

        return false;
    }
}
