using UnityEngine;
using System.Collections;

public class Util
{
    public static float Gain(float g)
    {
        return Mathf.Min(g * Time.deltaTime, 1.0f);
    }
}
