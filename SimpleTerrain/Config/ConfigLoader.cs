namespace SimpleTerrain.Config;

using System.Text.Json;
using System.Text.Json.Serialization;

using Utils.Misc;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        IncludeFields = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    public static AppConfig Load(string path)
    {
        var fullPath = AssetPath.Resolve(path);

        if (!File.Exists(fullPath))
        {
            Console.WriteLine("Config not found. Creating default config.");
            var defaultConfig = new AppConfig();
            
            // fullPath is already absolute here — AssetPath.Resolve is a no-op on absolute paths
            Save(defaultConfig, fullPath);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(fullPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);

            if (config == null)
                throw new Exception("Deserialized config is null");

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    public static void Save(AppConfig config, string path)
    {
        var fullPath = AssetPath.Resolve(path);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(fullPath, json);
        Console.WriteLine($"✅ Config saved to: {fullPath}");
    }
}