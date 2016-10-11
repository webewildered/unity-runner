using UnityEngine;
using System.Collections;

public class Rng
{
    System.Random random;

    public Rng(int seed)
    {
        random = new System.Random(seed);
    }

    public float Range(float min, float max)
    {
        return (float)random.NextDouble() * (max - min) + min;
    }

    public int Range(int min, int max)
    {
        return random.Next(min, max);
    }
}
