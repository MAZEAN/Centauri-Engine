namespace Centauri.Utils.Math;

using System;

public static class MathHelper
{
    public static float DegreesToRadians(float degrees)
    {
        return degrees * (MathF.PI / 180f);
    }
}