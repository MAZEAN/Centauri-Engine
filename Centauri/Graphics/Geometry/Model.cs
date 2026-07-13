namespace Centauri.Graphics.Geometry;

using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using System.Numerics;
using System.Text;

using Utils.Geometry;

using AssimpMesh = Silk.NET.Assimp.Mesh;

public sealed class MeshData
{
    public float[] Vertices { get; }
    public uint[]  Indices  { get; }
    public string  Name     { get; }
    
    public MeshData(float[] vertices, uint[] indices, string name = "")
    {
        Vertices = vertices;
        Indices  = indices;
        Name     = name;
    }
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
        Meshes = data.Meshes.Select(m => new Mesh(gl, m.Vertices, m.Indices, m.Name)).ToList();
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
            ProcessNode(scene->MRootNode, scene, data.Meshes, Matrix4x4.Identity);

            return data;
        }
        finally
        {
            assimp.Dispose();
        }
    }

    private static unsafe void ProcessNode(Node* node, Scene* scene, List<MeshData> meshes, Matrix4x4 parentWorld)
    {
        // Assimp's aiMatrix4x4 stores translation in a4/b4/c4 (row-major, column-vector
        // convention) — despite Silk.NET typing MTransformation as System.Numerics.Matrix4x4,
        // it's a raw field-order overlay of that same layout, not a converted one. System.Numerics
        // expects translation in M41/M42/M43 (row-vector convention), so using this matrix as-is
        // silently reads zero translation and treats the real translation as bogus rotation/scale
        // terms — verified empirically against a hand-built glTF with a known node translation.
        // Transposing puts it in the same convention Transform.cs uses everywhere else
        // (LocalMatrix * Parent.WorldMatrix), so the same left-to-right chaining works here too.
        var local = Matrix4x4.Transpose(node->MTransformation);
        var world = local * parentWorld;

        for (var i = 0; i < node->MNumMeshes; i++)
            meshes.Add(ProcessMesh(scene->MMeshes[node->MMeshes[i]], world));

        for (var i = 0; i < node->MNumChildren; i++)
            ProcessNode(node->MChildren[i], scene, meshes, world);
    }

    private static unsafe MeshData ProcessMesh(AssimpMesh* mesh, Matrix4x4 world)
    {
        // Normals/tangents need the inverse-transpose of the linear (3x3) part to stay correct
        // under non-uniform scale — using `world` directly would skew them under a squash/stretch
        // node. TransformNormal ignores the translation row/column either way (it only reads the
        // 3x3 part), so inverting the full 4x4 and transposing is equivalent to (and simpler than)
        // extracting just the 3x3 submatrix first. Falls back to `world` itself for the rare
        // degenerate (non-invertible, e.g. zero-scale) node rather than throwing.
        var normalMatrix = Matrix4x4.Invert(world, out var invWorld) ? Matrix4x4.Transpose(invWorld) : world;

        var vertices = new float[mesh->MNumVertices * 11];
        var v = 0;

        for (uint i = 0; i < mesh->MNumVertices; i++)
        {
            var position  = Vector3.Transform(mesh->MVertices[i], world);
            var normal    = mesh->MNormals  != null ? SafeNormalize(Vector3.TransformNormal(mesh->MNormals[i],  normalMatrix)) : Vector3.Zero;
            var tangent   = mesh->MTangents != null ? SafeNormalize(Vector3.TransformNormal(mesh->MTangents[i], normalMatrix)) : Vector3.Zero;
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

        var name = Encoding.UTF8.GetString(mesh->MName.Data, (int)mesh->MName.Length);
        return new MeshData(vertices, indices, name);
    }

    // Vector3.Normalize on a (near-)zero vector produces NaN, which would poison every downstream
    // lighting calculation for that vertex — a degenerate source normal/tangent, or a degenerate
    // (zero-scale) node transform, is rare but not impossible, so this guards against propagating
    // NaN rather than assuming TransformNormal's output is always safely normalizable.
    private static Vector3 SafeNormalize(Vector3 v)
    {
        var lenSq = v.LengthSquared();
        return lenSq > 1e-12f ? v / MathF.Sqrt(lenSq) : Vector3.Zero;
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