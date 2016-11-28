using UnityEngine;
using System.Collections;

public class MaterialManager : MonoBehaviour
{
    public Material material;
    public Light light;
	
	// Update is called once per frame
	void Update ()
    {
        Vector3 lightForward = light.transform.forward.normalized;
        Vector4 lightDirection = new Vector4(lightForward.x, lightForward.y, lightForward.z, 0.0f);
        lightDirection.Normalize();
        material.SetVector("_LightDirection", lightDirection);
	}
}
