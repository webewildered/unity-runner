using UnityEngine;
using System.Collections;

public class DoPost : MonoBehaviour
{
	private Material material;
    public Light shaderLight;
    
	void Awake()
	{
		material = new Material(Shader.Find("Hidden/RetroPost"));

        Camera camera = gameObject.GetComponent<Camera>();
        camera.depthTextureMode = DepthTextureMode.DepthNormals;

        float width = camera.targetTexture.width;
        float height = camera.targetTexture.height;
        material.SetFloat("_InvScreenWidth", 1.0f / width);
        material.SetFloat("_InvScreenHeight", 1.0f / height);
    }
    
	// Postprocess the image
	void OnRenderImage (RenderTexture source, RenderTexture destination)
    {
        Camera camera = gameObject.GetComponent<Camera>();
        Vector3 lightForward = shaderLight.transform.forward.normalized;
        Vector4 lightDirection = camera.worldToCameraMatrix * new Vector4(lightForward.x, lightForward.y, lightForward.z, 0.0f);

        lightDirection.Normalize();
        material.SetVector("_LightDirection", lightDirection);

        Graphics.Blit (source, destination, material);
	}
}
