namespace Centauri.UI.Panels.Inspector;

using World;

// One collapsible block of the Properties panel. Each implementation decides whether it
// renders anything for the current scene/selection (e.g. entity sections no-op when
// nothing is selected). Add a section = add a class + one line in PropertiesPanel.
public interface IInspectorSection
{
    void Draw(Scene scene);
}