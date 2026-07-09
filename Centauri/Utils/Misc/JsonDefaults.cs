namespace Centauri.Utils.Misc;

using System.Text.Json;
using System.Text.Json.Serialization;

public static class JsonDefaults
{
    // Shared by ConfigLoader and SceneLoader so config and scene parse/serialize
    // identically: case-insensitive reads, enum-by-name, indented + null-skipping writes.
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling      = JsonUnmappedMemberHandling.Disallow,
        Converters                  = { new JsonStringEnumConverter() }
    };
}