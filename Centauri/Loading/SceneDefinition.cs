namespace Centauri.Loading;

using System.Text.Json.Serialization;
using System.Text.Json;

public class SceneDefinition
{
    [JsonPropertyName("entities")] public List<EntityDefinition> Entities { get; set; } = [];
    [JsonPropertyName("cameras")]  public List<CameraDefinition> Cameras  { get; set; } = [];
    [JsonPropertyName("skybox")]   public List<SkyboxDefinition> Skyboxes { get; set; } = [];
}

public class EntityDefinition
{
    [JsonPropertyName("name")]     public string  Name     { get; set; } = "Entity";
    [JsonPropertyName("model")]    public string? Model    { get; set; }
    [JsonPropertyName("material")] public string? Material { get; set; }
    [JsonPropertyName("materials")] public MaterialBinding? Materials { get; set; }
    [JsonPropertyName("position")] public float[] Position { get; set; } = [0f, 0f, 0f];
    [JsonPropertyName("scale")]    public float[] Scale    { get; set; } = [1f, 1f, 1f];
    [JsonPropertyName("rotation")] public float[]? Rotation { get; set; }
    [JsonPropertyName("uvScale")]  public float[] UvScale  { get; set; } = [1f, 1f];
    [JsonPropertyName("uvOffset")] public float[] UvOffset { get; set; } = [0f, 0f];
    [JsonPropertyName("enabled")]  public bool    Enabled  { get; set; } = true;

    [JsonPropertyName("light")]    public LightDefinition? Light { get; set; }
    [JsonPropertyName("components")] public List<ComponentDefinition>? Components { get; set; }
}

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


public class LightDefinition
{
    [JsonPropertyName("type")]        public string  Type      { get; set; } = "point"; // directional|point|spot
    [JsonPropertyName("color")]       public float[] Color     { get; set; } = [1f, 1f, 1f];
    [JsonPropertyName("intensity")]   public float   Intensity { get; set; } = 1.0f;
    [JsonPropertyName("enabled")]     public bool    Enabled   { get; set; } = true;

    // directional + spot
    [JsonPropertyName("direction")]   public float[] Direction { get; set; } = [0f, -1f, 0f];

    // point
    [JsonPropertyName("constant")]    public float Constant  { get; set; } = 1.0f;
    [JsonPropertyName("linear")]      public float Linear    { get; set; } = 0.09f;
    [JsonPropertyName("quadratic")]   public float Quadratic { get; set; } = 0.032f;

    // spot
    [JsonPropertyName("innerCutoff")] public float InnerCutoff { get; set; } = 12.5f;
    [JsonPropertyName("outerCutoff")] public float OuterCutoff { get; set; } = 17.5f;
}

public class MaterialDefinition
{
    [JsonPropertyName("name")]     public string Name { get; set; } = "Default";
    [JsonPropertyName("shader")]    public string Shader { get; set; } = "";
    [JsonPropertyName("albedo")]    public string? Albedo    { get; set; }
    [JsonPropertyName("normal")]    public string? Normal    { get; set; }
    [JsonPropertyName("roughness")] public string? Roughness { get; set; }
    [JsonPropertyName("metallic")]  public string? Metallic  { get; set; }
    [JsonPropertyName("ao")]        public string? AO        { get; set; }
    [JsonPropertyName("opacity")]   public string? Opacity   { get; set; } // merged into albedo's alpha channel at load

    [JsonPropertyName("roughnessScalar")] public float RoughnessScalar { get; set; } = 0.5f;
    [JsonPropertyName("metallicScalar")]  public float MetallicScalar  { get; set; } = 0.1f;
    [JsonPropertyName("translucencyScalar")] public float TranslucencyScalar { get; set; } = 0f;
    [JsonPropertyName("color")]          public float[] Color        { get; set; } = [1f, 1f, 1f, 1f];
    [JsonPropertyName("twoSided")]       public bool   TwoSided      { get; set; } = false;
    [JsonPropertyName("wind")]           public bool   Wind          { get; set; } = false;
    [JsonPropertyName("triplanar")]      public bool   Triplanar      { get; set; } = false;
    [JsonPropertyName("triplanarScale")] public float  TriplanarScale { get; set; } = 1f;
}

public class CameraDefinition
{
    [JsonPropertyName("name")]     public string  Name     { get; set; } = "";
    [JsonPropertyName("position")] public float[] Position { get; set; } = [0f, 0f, 0f];
    [JsonPropertyName("up")]       public string  Up       { get; set; } = "Y";
    [JsonPropertyName("yaw")]      public float   Yaw      { get; set; }
    [JsonPropertyName("pitch")]    public float   Pitch    { get; set; }
    [JsonPropertyName("active")]   public bool Active { get; set; }
    [JsonPropertyName("primary")]  public bool Primary { get; set; }
}

public class SkyboxDefinition
{
    [JsonPropertyName("name")]     public string Name     { get; set; } = "Skybox";
    [JsonPropertyName("panorama")] public string Panorama { get; set; } = "";
    [JsonPropertyName("exposure")] public float  Exposure { get; set; } = 1.0f;
    [JsonPropertyName("blackLevel")] public float BlackLevel { get; set; } = 0.0f;
    [JsonPropertyName("active")]   public bool   Active   { get; set; }
}

public class ComponentDefinition
{
    [JsonPropertyName("type")]    public string Type    { get; set; } = "";
    [JsonPropertyName("enabled")] public bool   Enabled { get; set; } = true;

    // every field other than "type"/"enabled" lands here — each component reads its own
    [JsonExtensionData] public Dictionary<string, JsonElement>? Params { get; set; }
}