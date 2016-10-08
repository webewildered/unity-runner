using UnityEngine;
using System.Collections;

public class PxDouble : MonoBehaviour
{
    private Material material;
    private RenderTexture sourceTexture;
    public Camera SourceCamera;

    void Awake()
    {
        // Set up the post-process shader
        Shader shader = Shader.Find("Hidden/PxDoublePost");
        material = new Material(shader);

        // Configure the source camera to render to a half-res texture
        sourceTexture = new RenderTexture(Screen.width / 2, Screen.height / 2, 24);
        sourceTexture.filterMode = FilterMode.Point;
        SourceCamera.targetTexture = sourceTexture;
    }

    // Postprocess the image
    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Blit the half-res texture to the screen, doubling the pixels
        Graphics.Blit(sourceTexture, destination, material);
    }
}
