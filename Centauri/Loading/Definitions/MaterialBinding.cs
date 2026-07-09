namespace Centauri.Loading;

using System.Text.Json;
using System.Text.Json.Serialization;

// "materials" accepts either JSON shape:
//   "materials": ["bark.mat", "leaves.mat"]                      — positional, matched to mesh index
//   "materials": { "Bark": "bark.mat", "Leaves": "leaves.mat" }  — matched to mesh/node name from the model file
// Named binding removes the need to know/verify mesh order inside an exported model.
[JsonConverter(typeof(MaterialBindingConverter))]
public sealed class MaterialBinding
{
    public string[]?                 Indexed { get; init; }
    public Dictionary<string, string>? Named { get; init; }
}

public sealed class MaterialBindingConverter : JsonConverter<MaterialBinding>
{
    public override MaterialBinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.StartArray  => new MaterialBinding { Indexed = JsonSerializer.Deserialize<string[]>(ref reader, options) },
            JsonTokenType.StartObject => new MaterialBinding { Named   = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options) },
            _ => throw new JsonException($"'materials' must be a JSON array or object, got {reader.TokenType}.")
        };

    public override void Write(Utf8JsonWriter writer, MaterialBinding value, JsonSerializerOptions options)
    {
        if (value.Named is not null)
            JsonSerializer.Serialize(writer, value.Named, options);
        else
            JsonSerializer.Serialize(writer, value.Indexed ?? [], options);
    }
}