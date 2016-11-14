using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class SampledBlock
{
    [System.Serializable]
    public struct Sample
    {
        public Vector3 Left;
        public Vector3 Right;
    }
    public float Height;
    public List<Sample> Samples;

    public SampledBlock()
    {
        Samples = new List<Sample>();
    }

    ~SampledBlock()
    {

    }
}
