using UnityEngine;
using UnityEditor;
using System.Collections;

[CustomEditor(typeof(FloorGenerator))]
public class FloorGeneratorEditor : Editor
{
    int challenge;

    public FloorGeneratorEditor()
    {
        challenge = 0;
    }

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

        GUILayout.Label("Challenge:");
        int newChallenge;
        string challengeStr = GUILayout.TextField(challenge.ToString());
        if (int.TryParse(challengeStr, out newChallenge))
        {
            challenge = newChallenge;
        }

        if (GUILayout.Button("Reset"))
        {
            floorGenerator.Reset(seed);
            challenge = 0;
        }

        if (GUILayout.Button("Append"))
        {
            floorGenerator.Append(challenge);
        }

        if (GUILayout.Button("Append+"))
        {
            floorGenerator.Append(challenge++);
        }

        if (GUILayout.Button("Random"))
        {
            floorGenerator.AppendRandom(challenge);
        }

        if (GUILayout.Button("Random+"))
        {
            floorGenerator.AppendRandom(challenge++);
        }
    }
}
