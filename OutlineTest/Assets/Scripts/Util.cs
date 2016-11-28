using UnityEngine;
using System.Collections;

public class Util
{
    public static float Gain(float g)
    {
        return Mathf.Min(g * Time.deltaTime, 1.0f);
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
