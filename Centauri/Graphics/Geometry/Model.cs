namespace Centauri.Graphics.Geometry;

using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using System.Numerics;

using Utils.Geometry;

using AssimpMesh = Silk.NET.Assimp.Mesh;

public sealed class MeshData
{
    public MeshData(float[] vertices, uint[] indices)
    {
        Vertices = vertices;
        Indices  = indices;
    }

    public float[] Vertices { get; }
    public uint[]  Indices  { get; }
}

public sealed class ModelData
{
    public List<MeshData> Meshes         { get; } = new();
    public string         AssetDirectory { get; set; } = string.Empty;
}

public class Model : IDisposable
{
    public string      AssetDirectory { get; private set; } = string.Empty;
    
    public List<Mesh>  Meshes { get; private set; }
    public BoundingBox Bounds { get; private set; }

    // constructor for file-loaded models
    public Model(GL gl, ModelData data)
    {
        AssetDirectory = data.AssetDirectory;
        Meshes = data.Meshes.Select(m => new Mesh(gl, m.Vertices, m.Indices)).ToList();
        Bounds = ComputeBounds(Meshes);
    }
    
    public Model(GL gl, string path) : this(gl, Decode(path)) { }

    // constructor for code-generated models (floor plane, terrain etc.)
    public Model(GL gl, IEnumerable<Mesh> meshes)
    {
        Meshes = meshes.ToList();
        Bounds = ComputeBounds(Meshes); // compute bounds from provided meshes
    }

    public static unsafe ModelData Decode(string path)
    {
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException($"Model file not found: {path}");

        var assimp = Assimp.GetApi();
        try
        {
            var scene = assimp.ImportFile(path, (uint)(
                PostProcessSteps.Triangulate            |
                PostProcessSteps.GenerateNormals        |
                PostProcessSteps.CalculateTangentSpace  |
                PostProcessSteps.JoinIdenticalVertices
            ));

            if (scene == null
                || scene->MFlags == Assimp.SceneFlagsIncomplete
                || scene->MRootNode == null)
            {
                throw new Exception($"Assimp failed to load '{path}': {assimp.GetErrorStringS()}");
            }
            
            var data = new ModelData { AssetDirectory = Path.GetDirectoryName(path) ?? string.Empty };
            ProcessNode(scene->MRootNode, scene, data.Meshes);
            
            return data;
        }
        finally
        {
            assimp.Dispose();
        }
    }

    private static unsafe void ProcessNode(Node* node, Scene* scene, List<MeshData> meshes)
    {
        for (var i = 0; i < node->MNumMeshes; i++)
            meshes.Add(ProcessMesh(scene->MMeshes[node->MMeshes[i]]));

        for (var i = 0; i < node->MNumChildren; i++)
            ProcessNode(node->MChildren[i], scene, meshes);
    }

    private static unsafe MeshData ProcessMesh(AssimpMesh* mesh)
    {
        var vertices = new float[mesh->MNumVertices * 11];
        var v = 0;

        for (uint i = 0; i < mesh->MNumVertices; i++)
        {
            var position  = mesh->MVertices[i];
            var normal    = mesh->MNormals    != null ? mesh->MNormals[i]    : Vector3.Zero;
            var tangent   = mesh->MTangents   != null ? mesh->MTangents[i]   : Vector3.Zero;
            var texCoords = mesh->MTextureCoords[0] != null
                ? new Vector2(mesh->MTextureCoords[0][i].X, mesh->MTextureCoords[0][i].Y)
                : Vector2.Zero;

            vertices[v++] = position.X;
            vertices[v++] = position.Y;
            vertices[v++] = position.Z;
            vertices[v++] = normal.X;
            vertices[v++] = normal.Y;
            vertices[v++] = normal.Z;
            vertices[v++] = texCoords.X;
            vertices[v++] = texCoords.Y;
            vertices[v++] = tangent.X;
            vertices[v++] = tangent.Y;
            vertices[v++] = tangent.Z;
        }
        
        var indices = new uint[mesh->MNumFaces * 3];
        var idx = 0;

        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            var face = mesh->MFaces[i];
            for (uint j = 0; j < face.MNumIndices; j++)
                indices[idx++] = face.MIndices[j];
        }

        return new MeshData(vertices, indices);
    }

    private static BoundingBox ComputeBounds(List<Mesh> meshes)
    {
        if (meshes.Count == 0)
            return new BoundingBox(Vector3.Zero, Vector3.Zero);

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var mesh in meshes)
        {
            min = Vector3.Min(min, mesh.Bounds.Min);
            max = Vector3.Max(max, mesh.Bounds.Max);
        }

        return new BoundingBox(min, max);
    }

    public void Dispose()
    {
        foreach (var mesh in Meshes)
            mesh.Dispose();
    }
}