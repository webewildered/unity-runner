using UnityEngine;
using System.Collections;

public class Util
{
    public static float Gain(float g)
    {
        return Mathf.Min(g * Time.deltaTime, 1.0f);
    }

    public static Vector3 Interpolate(Vector3 x0, Vector3 x1, float t)
    {
        return x0 * (1.0f - t) + x1 * t;
    }

    public static Vector3 Up
    {
        get { return new Vector3(0, 1, 0); }
    }

    public static Vector3 Forward
    {
        get { return new Vector3(0, 0, 1); }
    }
}
