using System.Text.Json;

namespace CinePosto.Connectors;

/// <summary>Python-style reading of JSON values, used by the connectors that port legacy code which tests values for truth.</summary>
internal static class LegacyJson
{
    /// <summary>True when Python would treat the value as true: not null, false, zero, or an empty string, array or object.</summary>
    public static bool IsTruthy(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Undefined or JsonValueKind.Null or JsonValueKind.False => false,
        JsonValueKind.Number => element.GetDouble() != 0,
        JsonValueKind.String => !string.IsNullOrEmpty(element.GetString()),
        JsonValueKind.Array => element.GetArrayLength() != 0,
        JsonValueKind.Object => element.EnumerateObject().Any(),
        _ => true,
    };

    /// <summary>The value of a property of an object, or an undefined element when the object does not have it.</summary>
    public static JsonElement Property(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) ? value : default;

    /// <summary>
    /// Python: <c>value or ""</c> for a value used as text. A value that is true but not text raises, as it does in the
    /// legacy code as soon as it is used as text.
    /// </summary>
    public static string TruthyText(JsonElement element, string property)
    {
        JsonElement value = Property(element, property);
        if (!IsTruthy(value))
        {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException($"Cannot read {property} from a JSON {value.ValueKind} value.");
    }
}
