using UnityEngine;
using System.Collections;

public class InstructionsFader : MonoBehaviour {

    float time;

	void Start ()
    {
        time = 1.0f;
	}
	
	void Update ()
    {
        time -= Time.deltaTime;
        if (time < 0.0f)
        {
            Destroy(gameObject);
        }
        else if (time < 1.0f)
        {
            CanvasRenderer[] renderers = gameObject.GetComponentsInChildren<CanvasRenderer>();
            foreach (CanvasRenderer renderer in renderers)
            {
                renderer.SetAlpha(time);
            }
        }
	}
}
