namespace Centauri.Rendering.Resources;

using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using System.Numerics;

using Core;
using Utils.Geometry;

using AssimpMesh = Silk.NET.Assimp.Mesh;

public class Model : IDisposable
{
    private readonly GL      _gl;
    private readonly Assimp? _assimp;

    public string      AssetDirectory { get; private set; } = string.Empty;
    public List<Mesh>  Meshes    { get; private set; } = new();
    public BoundingBox Bounds    { get; private set; }

    // constructor for file-loaded models
    public Model(GL gl, string path)
    {
        _gl     = gl;
        _assimp = Assimp.GetApi();
        LoadModel(path);
    }

    // constructor for code-generated models (floor plane, terrain etc.)
    public Model(GL gl, IEnumerable<Mesh> meshes)
    {
        _gl    = gl;
        Meshes = meshes.ToList();
        Bounds = ComputeBounds(Meshes); // compute bounds from provided meshes
    }

    private unsafe void LoadModel(string path)
    {
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException($"Model file not found: {path}");

        var scene = _assimp!.ImportFile(path, (uint)(
            PostProcessSteps.Triangulate            |
            PostProcessSteps.GenerateNormals        |
            PostProcessSteps.CalculateTangentSpace  |
            PostProcessSteps.JoinIdenticalVertices
        ));

        if (scene == null
            || scene->MFlags == Assimp.SceneFlagsIncomplete
            || scene->MRootNode == null)
        {
            throw new Exception($"Assimp failed to load '{path}': {_assimp.GetErrorStringS()}");
        }

        AssetDirectory = Path.GetDirectoryName(path) ?? string.Empty;
        ProcessNode(scene->MRootNode, scene);
        Bounds = ComputeBounds(Meshes); // assign after all meshes are loaded
    }

    private unsafe void ProcessNode(Node* node, Scene* scene)
    {
        for (var i = 0; i < node->MNumMeshes; i++)
            Meshes.Add(ProcessMesh(scene->MMeshes[node->MMeshes[i]]));

        for (var i = 0; i < node->MNumChildren; i++)
            ProcessNode(node->MChildren[i], scene);
    }

    private unsafe Mesh ProcessMesh(AssimpMesh* mesh)
    {
        var vertices = new List<Vertex>(capacity: (int)mesh->MNumVertices);
        var indices  = new List<uint>();

        for (uint i = 0; i < mesh->MNumVertices; i++)
        {
            vertices.Add(new Vertex
            {
                Position  = mesh->MVertices[i],
                Normal    = mesh->MNormals    != null ? mesh->MNormals[i]    : Vector3.Zero,
                Tangent   = mesh->MTangents   != null ? mesh->MTangents[i]   : Vector3.Zero,
                Bitangent = mesh->MBitangents != null ? mesh->MBitangents[i] : Vector3.Zero,
                TexCoords = mesh->MTextureCoords[0] != null
                    ? new Vector2(mesh->MTextureCoords[0][i].X, mesh->MTextureCoords[0][i].Y)
                    : Vector2.Zero
            });
        }

        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            var face = mesh->MFaces[i];
            for (uint j = 0; j < face.MNumIndices; j++)
                indices.Add(face.MIndices[j]);
        }

        return new Mesh(_gl, BuildVertices(vertices), BuildIndices(indices));
    }

    private static float[] BuildVertices(List<Vertex> vertexCollection)
    {
        // 11 floats per vertex: pos(3) + normal(3) + uv(2) + tangent(3)
        var vertices = new float[vertexCollection.Count * 11];
        int i = 0;

        foreach (var v in vertexCollection)
        {
            vertices[i++] = v.Position.X;
            vertices[i++] = v.Position.Y;
            vertices[i++] = v.Position.Z;
            vertices[i++] = v.Normal.X;
            vertices[i++] = v.Normal.Y;
            vertices[i++] = v.Normal.Z;
            vertices[i++] = v.TexCoords.X;
            vertices[i++] = v.TexCoords.Y;
            vertices[i++] = v.Tangent.X;
            vertices[i++] = v.Tangent.Y;
            vertices[i++] = v.Tangent.Z;
        }

        return vertices;
    }

    private static uint[] BuildIndices(List<uint> indices) => indices.ToArray();

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

        _assimp?.Dispose(); // null-safe — not created for code-generated models
    }
}