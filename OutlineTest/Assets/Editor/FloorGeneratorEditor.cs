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
        if (GUILayout.Button("Build Mesh"))
        {
            floorGenerator.Rebuild();
        }
    }
}
