using UnityEngine;
using System.Collections;

public class RemoveTimer : MonoBehaviour
{
    public float Timer;

	// Update is called once per frame
	void Update ()
    {
        Timer -= Time.deltaTime;
        if (Timer <= 0.0f)
        {
            Destroy(gameObject);
        }
	}
}
