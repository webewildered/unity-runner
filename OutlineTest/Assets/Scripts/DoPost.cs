using UnityEngine;
using System.Collections;

public class DoPost : MonoBehaviour
{
	private Material material;
    public Light shaderLight;

    public GameObject EdgeCamera;
    Camera cam;


    void Awake()
	{
		material = new Material(Shader.Find("Hidden/RetroPost"));

        cam = gameObject.GetComponent<Camera>();
        cam.depthTextureMode = DepthTextureMode.DepthNormals;

        float width = cam.targetTexture.width;
        float height = cam.targetTexture.height;
        material.SetFloat("_InvScreenWidth", 1.0f / width);
        material.SetFloat("_InvScreenHeight", 1.0f / height);
    }
    
	// Postprocess the image
	void OnRenderImage (RenderTexture source, RenderTexture destination)
    {
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
        ///Matrix4x4 viewProj = proj * cam.worldToCameraMatrix;
        //material.SetMatrix("_InvVP", viewProj.inverse);
        material.SetMatrix("_InvProjection", proj.inverse);

        material.SetTexture("_EdgeTexture", EdgeCamera.GetComponent<EdgeCamera>().Target);
        Graphics.Blit (source, destination, material);
	}
}
