using UnityEngine;
using System.Collections;

public class DeathSnapshot : MonoBehaviour
{
    public GameObject MainCamera;
    public Texture2D Texture;
    Camera cam;

    void Awake()
    {
        // Configure the source camera to render to a half-res texture
        int width = Screen.width / 2;
        int height = Screen.height / 2;
        RenderTexture target = new RenderTexture(width, height, 24);
        target.filterMode = FilterMode.Point;

        cam = GetComponent<Camera>();
        cam.targetTexture = target;
        cam.enabled = false;

        Texture = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    void Update()
    {
        transform.position = MainCamera.transform.position;
        transform.rotation = MainCamera.transform.rotation;
    }
}
