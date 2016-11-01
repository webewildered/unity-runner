using UnityEngine;
using System.Collections;

public class EdgeCamera : MonoBehaviour
{
    public RenderTexture Target;
    public GameObject MainCamera;
    public Shader Shader;

    void Start ()
    {
        int width = Screen.width / 2;
        int height = Screen.height / 2;
        Target = new RenderTexture(width, height, 24);

        Camera camera = GetComponent<Camera>();
        camera.SetReplacementShader(Shader, "");
        camera.targetTexture = Target;
	}
	
	void LateUpdate ()
    {
        transform.position = MainCamera.transform.position;
        transform.rotation = MainCamera.transform.rotation;
	}
}
