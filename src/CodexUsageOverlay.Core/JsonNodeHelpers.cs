using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexUsageOverlay.Core;

internal static class JsonNodeHelpers
{
    public static JsonNode? Payload(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return node;
        }

        return obj["params"] ?? obj["result"] ?? node;
    }

    public static double? DirectNumber(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var value))
            {
                var number = AsNumber(value);
                if (number.HasValue)
                {
                    return number;
                }
            }
        }

        return null;
    }

    public static string? DirectString(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (obj.TryGetPropertyValue(name, out var value) && value is not null)
            {
                string text;
                try
                {
                    text = value.GetValue<string>();
                }
                catch
                {
                    text = value.ToString();
                    if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
                    {
                        try
                        {
                            text = JsonSerializer.Deserialize<string>(text) ?? text;
                        }
                        catch
                        {
                            text = text.Trim('"');
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    public static IEnumerable<JsonObject> FindObjectsInArrays(JsonNode? node, params string[] arrayNames)
    {
        if (node is null)
        {
            yield break;
        }

        if (node is JsonArray rootArray)
        {
            foreach (var item in rootArray.OfType<JsonObject>())
            {
                yield return item;
            }
        }

        if (node is not JsonObject obj)
        {
            yield break;
        }

        foreach (var name in arrayNames)
        {
            if (obj.TryGetPropertyValue(name, out var candidate) && candidate is JsonArray array)
            {
                foreach (var item in array.OfType<JsonObject>())
                {
                    yield return item;
                }
            }
        }

        foreach (var child in obj)
        {
            foreach (var found in FindObjectsInArrays(child.Value, arrayNames))
            {
                yield return found;
            }
        }
    }

    private static double? AsNumber(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<double>();
        }
        catch
        {
            var text = node.ToString();
            return double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;
        }
    }
}
