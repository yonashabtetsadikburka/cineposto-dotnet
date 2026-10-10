using System.Text;

namespace CinePosto.Domain.Text;

/// <summary>Faithful port of the legacy Python edit distance (parity first, see ADR 0002).</summary>
public static class EditDistance
{
    /// <summary>Computes the Levenshtein distance between two strings (iterative, O(n*m)).</summary>
    public static int Levenshtein(string a, string b)
    {
        // The legacy code counts Unicode code points, so compare runes rather than UTF-16 units.
        Rune[] first = a.EnumerateRunes().ToArray();
        Rune[] second = b.EnumerateRunes().ToArray();

        if (first.Length < second.Length)
        {
            return Levenshtein(b, a);
        }

        if (second.Length == 0)
        {
            return first.Length;
        }

        int[] previousRow = new int[second.Length + 1];
        for (int j = 0; j <= second.Length; j++)
        {
            previousRow[j] = j;
        }

        for (int i = 0; i < first.Length; i++)
        {
            int[] currentRow = new int[second.Length + 1];
            currentRow[0] = i + 1;
            for (int j = 0; j < second.Length; j++)
            {
                int cost = first[i] == second[j] ? 0 : 1;
                currentRow[j + 1] = Math.Min(
                    Math.Min(previousRow[j + 1] + 1, currentRow[j] + 1),
                    previousRow[j] + cost);
            }

            previousRow = currentRow;
        }

        return previousRow[second.Length];
    }
}
