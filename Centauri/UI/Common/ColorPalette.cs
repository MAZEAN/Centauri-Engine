namespace Centauri.UI.Common;

using System.Numerics;

internal static class ColorPalette
{
    // General
    public static readonly Vector4 Amber   = new(1.00f, 0.75f, 0.20f, 1f);
    public static readonly Vector4 Green   = new(0.45f, 0.90f, 0.45f, 1f);
    public static readonly Vector4 Blue    = new(0.40f, 0.70f, 1.00f, 1f);
    public static readonly Vector4 Red     = new(1.00f, 0.35f, 0.35f, 1f);
    public static readonly Vector4 Purple  = new(0.70f, 0.50f, 1.00f, 1f);
    public static readonly Vector4 White   = Vector4.One;
    
    // Theme specific
    public static readonly Vector4 Text      = new(0.90f, 0.90f, 0.90f, 1.00f);
    public static readonly Vector4 TextDim   = new(0.55f, 0.55f, 0.55f, 1.00f);
    public static readonly Vector4 WindowBg  = new(0.17f, 0.17f, 0.17f, 1.00f);
    public static readonly Vector4 PanelBg   = new(0.21f, 0.21f, 0.21f, 1.00f);
    public static readonly Vector4 Field     = new(0.28f, 0.28f, 0.28f, 1.00f);
    public static readonly Vector4 FieldHi   = new(0.33f, 0.33f, 0.33f, 1.00f);
    public static readonly Vector4 Header    = new(0.30f, 0.30f, 0.30f, 1.00f);
    public static readonly Vector4 HeaderHi  = new(0.36f, 0.36f, 0.36f, 1.00f);
    public static readonly Vector4 Accent    = new(0.22f, 0.46f, 0.80f, 1.00f);
    public static readonly Vector4 AccentDim = new(0.20f, 0.39f, 0.66f, 1.00f);
    public static readonly Vector4 Line      = new(0.10f, 0.10f, 0.10f, 1.00f);
}