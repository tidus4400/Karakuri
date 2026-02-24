using System.Text.Json;

namespace tidus4400.Karakuri.Shared;

public static class JsonHelpers
{
    public static string? GetString(Dictionary<string, object?> config, string key)
    {
        return TryGet(config, key) switch
        {
            null => null,
            string s => s,
            JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString(),
            JsonElement el => el.ToString(),
            _ => TryGet(config, key)?.ToString()
        };
    }

    public static int? GetInt(Dictionary<string, object?> config, string key)
    {
        var value = TryGet(config, key);
        return value switch
        {
            null => null,
            int i => i,
            long l => (int)l,
            JsonElement el when el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i) => i,
            JsonElement el when el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var j) => j,
            string s when int.TryParse(s, out var k) => k,
            _ => null
        };
    }

    private static object? TryGet(Dictionary<string, object?> config, string key)
    {
        config.TryGetValue(key, out var value);
        return value;
    }
}
