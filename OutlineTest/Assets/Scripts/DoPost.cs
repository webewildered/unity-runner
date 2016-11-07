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

        //test
        Vector4 testPosition = new Vector4(0.5f, 0.0f, 9.5f, 1.0f);
        Vector4 testPositionView = cam.worldToCameraMatrix * testPosition;
        Vector4 projPositionRhw = proj * testPositionView;
        Vector3 projPosition = new Vector3(projPositionRhw.x, projPositionRhw.y, projPositionRhw.z) * (1.0f / projPositionRhw.w);
        Vector4 testRecalc = projPosition; testRecalc.w = 1.0f;
        Vector4 testResult = proj.inverse * testRecalc;

        Vector4 projPositionRhw2 = proj * testPositionView;
        Vector3 projPosition2 = new Vector3(projPositionRhw2.x, projPositionRhw2.y, projPositionRhw2.z) * (1.0f / projPositionRhw2.w);

        material.SetTexture("_EdgeTexture", EdgeCamera.GetComponent<EdgeCamera>().Target);
        Graphics.Blit (source, destination, material);
	}
}
