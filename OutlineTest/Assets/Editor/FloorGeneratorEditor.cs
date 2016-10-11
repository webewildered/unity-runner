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
        if (GUILayout.Button("Build Curve"))
        {
            floorGenerator.Rebuild();
        }

        if (GUILayout.Button("Build Wave"))
        {
            floorGenerator.RebuildWave();
        }

        if (GUILayout.Button("Build Open"))
        {
            floorGenerator.RebuildOpen();
        }

        if (GUILayout.Button("Build Field"))
        {
            floorGenerator.RebuildField();
        }
    }
}
