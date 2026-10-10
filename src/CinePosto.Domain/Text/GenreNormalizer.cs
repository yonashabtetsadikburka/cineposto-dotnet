using System.Text.Json;
using System.Text.RegularExpressions;

namespace CinePosto.Domain.Text;

/// <summary>Faithful port of the legacy Python genre normalizer (parity first, see ADR 0002).</summary>
public static partial class GenreNormalizer
{
    /// <summary>Normalizes raw genre data to a list of genre names.</summary>
    public static IReadOnlyList<string> Normalize(JsonElement raw)
    {
        if (raw.ValueKind == JsonValueKind.String)
        {
            return GenreSeparator().Split(raw.GetString() ?? string.Empty)
                .Select(g => g.Trim())
                .Where(g => g.Length > 0)
                .ToList();
        }

        if (raw.ValueKind == JsonValueKind.Array)
        {
            List<string> genres = new();
            foreach (JsonElement item in raw.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    genres.Add(item.GetString() ?? string.Empty);
                }
                else if (IsFalsy(item))
                {
                    continue;
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    genres.Add(item.TryGetProperty("name", out JsonElement name) && name.ValueKind == JsonValueKind.String
                        ? name.GetString() ?? string.Empty
                        : string.Empty);
                }
                else
                {
                    // The legacy code calls .get on truthy non-objects and crashes with AttributeError.
                    throw new InvalidOperationException($"Cannot read a genre name from a JSON {item.ValueKind} value.");
                }
            }

            return genres;
        }

        return [];
    }

    private static bool IsFalsy(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => true,
        JsonValueKind.Number => element.GetDouble() == 0,
        JsonValueKind.String => string.IsNullOrEmpty(element.GetString()),
        JsonValueKind.Array => element.GetArrayLength() == 0,
        JsonValueKind.Object => !element.EnumerateObject().Any(),
        _ => false,
    };

    // Original Python: re.split(r"[,/]", raw)
    [GeneratedRegex("[,/]")]
    private static partial Regex GenreSeparator();
}
