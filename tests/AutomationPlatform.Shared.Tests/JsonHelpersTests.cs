using System.Text.Json;
using AutomationPlatform.Shared;

namespace AutomationPlatform.Shared.Tests;

public sealed class JsonHelpersTests
{
    [Fact]
    public void GetString_ReadsString_AndJsonElement()
    {
        var doc = JsonDocument.Parse("""{"path":"dotnet","timeoutSec":"30"}""");
        var config = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["path"] = "pwsh",
            ["args"] = doc.RootElement.GetProperty("path").Clone()
        };

        Assert.Equal("pwsh", JsonHelpers.GetString(config, "path"));
        Assert.Equal("dotnet", JsonHelpers.GetString(config, "args"));
        Assert.Equal("dotnet", JsonHelpers.GetString(config, "ARGS"));
    }

    [Fact]
    public void GetInt_ReadsPrimitive_String_AndJsonElement()
    {
        var numberDoc = JsonDocument.Parse("""{"timeoutSec":45}""");
        var stringDoc = JsonDocument.Parse("""{"timeoutSec":"60"}""");

        var config = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = 12,
            ["b"] = 34L,
            ["c"] = "56",
            ["d"] = numberDoc.RootElement.GetProperty("timeoutSec").Clone(),
            ["e"] = stringDoc.RootElement.GetProperty("timeoutSec").Clone()
        };

        Assert.Equal(12, JsonHelpers.GetInt(config, "a"));
        Assert.Equal(34, JsonHelpers.GetInt(config, "b"));
        Assert.Equal(56, JsonHelpers.GetInt(config, "c"));
        Assert.Equal(45, JsonHelpers.GetInt(config, "d"));
        Assert.Equal(60, JsonHelpers.GetInt(config, "e"));
    }

    [Fact]
    public void GetInt_InvalidValue_ReturnsNull()
    {
        var doc = JsonDocument.Parse("""{"timeoutSec":true}""");
        var config = new Dictionary<string, object?> { ["timeoutSec"] = doc.RootElement.GetProperty("timeoutSec").Clone() };

        Assert.Null(JsonHelpers.GetInt(config, "timeoutSec"));
        Assert.Null(JsonHelpers.GetInt(config, "missing"));
    }
}
