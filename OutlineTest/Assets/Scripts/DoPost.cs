using UnityEngine;
using System.Collections;

public class DoPost : MonoBehaviour
{
	private Material material;
    public Light shaderLight;

    public GameObject EdgeCamera;
    
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
        material.SetTexture("_EdgeTexture", EdgeCamera.GetComponent<EdgeCamera>().Target);
        Graphics.Blit (source, destination, material);
	}
}
