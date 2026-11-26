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
}
