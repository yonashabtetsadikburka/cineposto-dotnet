using CinePosto.Domain.Models;

namespace CinePosto.Domain.Showings;

/// <summary>
/// Merges showings that differ only in their start times. Port of the legacy output step
/// (<c>models.py</c>, <c>_consolidate_showings</c>; parity first, see ADR 0002).
/// </summary>
public static class ShowingConsolidator
{
    /// <summary>
    /// Returns one showing for each cinema, day, language, screen and set of session attributes, in the order in which
    /// each of them first appears. Its times are sorted and listed once, and its address is the first one that is not
    /// empty. A missing and an empty language or screen count as the same.
    /// </summary>
    /// <remarks>
    /// The showings given are those of one film. As in the legacy code, attributes are compared as one text joined by
    /// commas, so an attribute that contains a comma can match two separate attributes.
    /// </remarks>
    public static IReadOnlyList<ScrapedShowing> Consolidate(IEnumerable<ScrapedShowing> showings)
    {
        ArgumentNullException.ThrowIfNull(showings);

        List<Group> groups = [];
        Dictionary<(string Cinema, DateOnly Date, string Language, string Screen, string Attributes), Group> byKey = [];
        foreach (ScrapedShowing showing in showings)
        {
            var key = (
                showing.CinemaSlug,
                showing.Date,
                showing.Language ?? string.Empty,
                showing.Screen ?? string.Empty,
                string.Join(',', showing.SessionAttributes.Order(StringComparer.Ordinal)));
            if (!byKey.TryGetValue(key, out Group? group))
            {
                group = new Group(showing);
                byKey[key] = group;
                groups.Add(group);
            }

            group.Add(showing);
        }

        return groups.Select(group => group.ToShowing()).ToList();
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

    private sealed class Group(ScrapedShowing first)
    {
        private readonly SortedSet<TimeOnly> _times = [];

        // The legacy code starts from a copy of the first list, which keeps what that list repeats or leaves empty.
        private readonly List<string> _attributes = [.. first.SessionAttributes];

        private string? _sourceUrl = first.SourceUrl;

        public void Add(ScrapedShowing showing)
        {
            _times.UnionWith(showing.Times);

            if (string.IsNullOrEmpty(_sourceUrl) && !string.IsNullOrEmpty(showing.SourceUrl))
            {
                _sourceUrl = showing.SourceUrl;
            }

            foreach (string attribute in showing.SessionAttributes)
            {
                if (attribute.Length > 0 && !_attributes.Contains(attribute, StringComparer.Ordinal))
                {
                    _attributes.Add(attribute);
                }
            }
        }

        public ScrapedShowing ToShowing() => first with
        {
            Times = [.. _times],
            Screen = NullIfEmpty(first.Screen),
            Language = NullIfEmpty(first.Language),
            SourceUrl = NullIfEmpty(_sourceUrl),
            SessionAttributes = _attributes,
        };
    }
}
