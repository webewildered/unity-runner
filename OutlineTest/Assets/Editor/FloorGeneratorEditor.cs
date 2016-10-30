using UnityEngine;
using UnityEditor;
using System.Collections;

[CustomEditor(typeof(FloorGenerator))]
public class FloorGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        FloorGenerator floorGenerator = (FloorGenerator)target;

        GUILayout.Label("Seed:");

        string seedStr = GUILayout.TextField("1234567890");
        int seed;
        bool seedOk = int.TryParse(seedStr, out seed);
        if (!seedOk)
        {
            seed = 1234567890;
        }

        if (GUILayout.Button("Reset"))
        {
            floorGenerator.Reset(seed);
        }

        for (int i = 0; i < 10; i++)
        {
            if (GUILayout.Button("Append " + i))
            {
                floorGenerator.Append(i);
            }
        }
    }
}
