using System;
using UnityEngine;

public static class MathHelper {
    public const float Pi = (float)Math.PI;
    public const float PiOver2 = (float)(Math.PI / 2);
    public const float PiOver4 = (float)(Math.PI / 4);

    public static bool AlmostEquals(float a, float b, float epsilon) {
        return Math.Abs(a - b) <= epsilon;
    }
    public static bool AlmostEquals(Vector3 a, Vector3 b, float epsilon) {
        return AlmostEquals(a.x, b.x, epsilon) && AlmostEquals(a.y, b.y, epsilon) && AlmostEquals(a.z, b.z, epsilon);
    }
}
