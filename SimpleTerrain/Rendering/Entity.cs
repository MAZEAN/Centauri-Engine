namespace SimpleTerrain.Rendering;

using Scene;

public class Entity
{
    public Model Model { get; }
    public Material Material { get; }
    public Transform Transform { get; set; }

    public Entity(Model model, Material material)
    {
        Model = model;
        Material = material;
        Transform = new Transform();
    }
    
    public void Dispose()
    {
        Material.Dispose();
    }
}