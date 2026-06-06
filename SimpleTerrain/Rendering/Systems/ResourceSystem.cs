namespace SimpleTerrain.Rendering.Systems;

using Silk.NET.OpenGL;

using Utils.Caching;
using Resources;
using Config;

public class ResourceSystem : IDisposable
{
    public AssetCache<GLTexture> Textures { get; }
    public AssetCache<GLShader> Shaders { get; }
    public AssetCache<Model> Models { get; }

    public ResourceSystem(GL gl, AppConfig config)
    {
        Textures = new AssetCache<GLTexture>(
            config.Render.TextureCacheSize,
            path => new GLTexture(gl, Path.GetFullPath(path))
        );

        Shaders = new AssetCache<GLShader>(
            config.Render.ShaderCacheSize,
            shaderBase =>
                new GLShader(
                    gl,
                    shaderBase + ".vert",
                    shaderBase + ".frag"));

        Models = new AssetCache<Model>(
            config.Render.ModelCacheSize,
            path => new Model(gl, path));
    }

    public void Dispose()
    {
        Textures.Dispose();
        Shaders.Dispose();
        Models.Dispose();

        Console.WriteLine("[ResourceSystem] Disposed all resources");
    }
}