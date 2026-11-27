using System.Text.RegularExpressions;

namespace CinePosto.Connectors;

/// <summary>Port of Python's <c>urllib.parse.urljoin</c>, which the legacy connectors use to complete addresses.</summary>
/// <remarks>
/// <see cref="Uri"/> is not used on purpose: it escapes spaces and non-ASCII characters and rewrites the address, so the output would differ.
/// Path parameters (<c>;name=value</c>) are not split off: they stay in the path, which gives the same text unless the path is only a parameter.
/// </remarks>
internal static partial class LegacyUrlResolver
{
    // Python: uses_relative (schemes whose addresses can be relative). Every one of them is also in uses_netloc.
    private static readonly HashSet<string> RelativeSchemes = new(StringComparer.Ordinal)
    {
        string.Empty, "ftp", "http", "gopher", "nntp", "imap", "wais", "file", "https", "shttp", "mms",
        "prospero", "rtsp", "rtspu", "sftp", "svn", "svn+ssh", "ws", "wss",
    };

    // Python: the C0 control characters and the space.
    private static readonly char[] ControlsAndSpace = [.. Enumerable.Range(0, 0x21).Select(c => (char)c)];

    /// <summary>Resolves <paramref name="url"/> against <paramref name="baseUrl"/> the way Python does.</summary>
    public static string Resolve(string baseUrl, string url)
    {
        if (baseUrl.Length == 0)
        {
            return url;
        }

        if (url.Length == 0)
        {
            return baseUrl;
        }

        Parts baseParts = Split(baseUrl, string.Empty);
        Parts target = Split(RemoveUnsafeCharacters(url), baseParts.Scheme);

        if (!string.Equals(target.Scheme, baseParts.Scheme, StringComparison.Ordinal) || !RelativeSchemes.Contains(target.Scheme))
        {
            return url;
        }

        string netloc = target.Netloc;
        if (netloc.Length > 0)
        {
            return Join(target.Scheme, netloc, target.Path, target.Query, target.Fragment);
        }

        netloc = baseParts.Netloc;

        if (target.Path.Length == 0)
        {
            string query = target.Query.Length > 0 ? target.Query : baseParts.Query;
            return Join(target.Scheme, netloc, baseParts.Path, query, target.Fragment);
        }

        List<string> baseSegments = [.. baseParts.Path.Split('/')];
        if (baseSegments[^1].Length != 0)
        {
            // The last item is not a directory, so it is not part of the resolution.
            baseSegments.RemoveAt(baseSegments.Count - 1);
        }

        List<string> segments;
        if (target.Path[0] == '/')
        {
            segments = [.. target.Path.Split('/')];
        }
        else
        {
            segments = [.. baseSegments, .. target.Path.Split('/')];
            if (segments.Count > 2)
            {
                // Drop empty items in the middle, so that joining does not repeat a slash.
                List<string> middle = segments.Skip(1).Take(segments.Count - 2).Where(s => s.Length > 0).ToList();
                segments = [segments[0], .. middle, segments[^1]];
            }
        }

        List<string> resolved = [];
        foreach (string segment in segments)
        {
            if (segment == "..")
            {
                if (resolved.Count > 0)
                {
                    resolved.RemoveAt(resolved.Count - 1);
                }
            }
            else if (segment != ".")
            {
                resolved.Add(segment);
            }
        }

        if (segments[^1] is "." or "..")
        {
            resolved.Add(string.Empty);
        }

        string path = string.Join('/', resolved);
        return Join(target.Scheme, netloc, path.Length == 0 ? "/" : path, target.Query, target.Fragment);
    }

    private readonly record struct Parts(string Scheme, string Netloc, string Path, string Query, string Fragment);

    // Python: urlsplit (recent versions drop leading control characters and spaces, and tab, CR and LF anywhere).
    private static string RemoveUnsafeCharacters(string url)
    {
        string trimmed = url.TrimStart(ControlsAndSpace);
        return trimmed.Replace("\t", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }

    // Python: urlsplit. Netloc is read before the fragment and the query, which are split at the first '#' and the first '?'.
    private static Parts Split(string url, string defaultScheme)
    {
        string scheme = defaultScheme;
        Match prefix = SchemePrefix().Match(url);
        if (prefix.Success)
        {
            scheme = url[..(prefix.Length - 1)].ToLowerInvariant();
            url = url[prefix.Length..];
        }

        string netloc = string.Empty;
        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            int end = url.IndexOfAny(['/', '?', '#'], 2);
            if (end < 0)
            {
                end = url.Length;
            }

            netloc = url[2..end];
            url = url[end..];
        }

        string fragment = string.Empty;
        int hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = url[(hash + 1)..];
            url = url[..hash];
        }

        string query = string.Empty;
        int question = url.IndexOf('?', StringComparison.Ordinal);
        if (question >= 0)
        {
            query = url[(question + 1)..];
            url = url[..question];
        }

        return new Parts(scheme, netloc, url, query, fragment);
    }

    // Python: urlunsplit. Empty query and fragment markers are dropped.
    private static string Join(string scheme, string netloc, string path, string query, string fragment)
    {
        string url = path;
        if (netloc.Length > 0 || (scheme.Length > 0 && RelativeSchemes.Contains(scheme) && !url.StartsWith("//", StringComparison.Ordinal)))
        {
            if (url.Length > 0 && url[0] != '/')
            {
                url = "/" + url;
            }

            url = "//" + netloc + url;
        }

        if (scheme.Length > 0)
        {
            url = scheme + ":" + url;
        }

        if (query.Length > 0)
        {
            url += "?" + query;
        }

        if (fragment.Length > 0)
        {
            url += "#" + fragment;
        }

        return url;
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.\\-]*:")]
    private static partial Regex SchemePrefix();
}
