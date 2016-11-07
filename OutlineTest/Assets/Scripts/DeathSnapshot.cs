using UnityEngine;
using System.Collections;

public class DeathSnapshot : MonoBehaviour
{
    public GameObject MainCamera;
    public Shader Shader;
    Camera cam;

    public Texture2D Texture;
    public RenderTexture Target;

    Vector3 velocity;

    void Awake()
    {
        // Configure the source camera to render to a half-res texture
        int width = Screen.width / 2;
        int height = Screen.height / 2;
        Target = new RenderTexture(width, height, 24);
        Target.filterMode = FilterMode.Point;

        cam = GetComponent<Camera>();
        cam.targetTexture = Target;
        cam.enabled = false;
        cam.SetReplacementShader(Shader, "");

        Texture = new Texture2D(width, height, TextureFormat.RGB24, false);
    }

    void LateUpdate()
    {
        transform.position = MainCamera.transform.position;
        transform.rotation = MainCamera.transform.rotation;
    }

    public void Snap(Vector3 velocity)
    {
        cam.enabled = true;
        this.velocity = velocity;
    }

    void OnPostRender()
    {
        Rect source = new Rect(0, 0, Texture.width, Texture.height);
        Texture.ReadPixels(source, 0, 0);
        Color[] colors = Texture.GetPixels();

        float invWidth = 1.0f / Texture.width;
        float invHeight = 1.0f / Texture.height;

        float variationSpeed = velocity.magnitude * 0.15f;

        const int maxParticles = 10000;
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[maxParticles];
        int numParticles = 0;
        for (int i = 0; i < colors.Length; i++)
        {
            if (colors[i] != Color.white)
            {
                const float size = 0.1f;
                const float lifetime = float.MaxValue;

                // Get the pixel position in viewport space
                float depth = colors[i].r + colors[i].g / 255.0f;
                float z = depth * cam.farClipPlane;// * (cam.farClipPlane - cam.nearClipPlane) + cam.nearClipPlane;// + Random.Range(0.0f, 0.4f);
                float x = (i % Texture.width) * invWidth;
                float y = (i / Texture.width) * invHeight;
                Vector4 posViewport = new Vector4(x, y, z);
                Vector4 posWorld = cam.ViewportToWorldPoint(posViewport);

                // Create the particle
                particles[numParticles].position = (Vector3)posWorld - velocity * Time.deltaTime;
                particles[numParticles].lifetime = lifetime;
                particles[numParticles].startLifetime = lifetime;
                particles[numParticles].startSize = size;
                particles[numParticles].startColor = new Color32(255, 0, 128, 255);
                particles[numParticles].startSize3D = new Vector3(size, size, size);
                particles[numParticles].velocity = velocity + new Vector3(Random.Range(-variationSpeed, variationSpeed), Random.Range(-variationSpeed, variationSpeed), Random.Range(-variationSpeed, variationSpeed));

                //particles[numParticles].randomSeed
                //particles[numParticles].rotation
                //particles[numParticles].rotation3D
                //particles[numParticles].velocity
                //particles[numParticles].angularVelocity
                //particles[numParticles].angularVelocity3D

                numParticles++;
                if (numParticles == maxParticles)
                {
                    break;
                }
            }
        }

        // Create the particle system
        GameObject pso = new GameObject();
        ParticleSystem ps = pso.AddComponent<ParticleSystem>();
        ps.gravityModifier = 1.0f;
        ps.loop = false;
        ps.maxParticles = numParticles;
        ps.SetParticles(particles, numParticles);
        ps.GetComponent<Renderer>().material = Resources.Load<Material>("ParticleMaterial");

        ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.separateAxes = false;
        limit.limit = new ParticleSystem.MinMaxCurve(0.0f);
        limit.dampen = 0.03f;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = false;

        ParticleSystem.CollisionModule collision = ps.collision;
        collision.enabled = true;
        collision.bounce = 0.2f;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        //collision.enableInteriorCollisions = true;
        //collision.enableDynamicColliders = true;
        collision.type = ParticleSystemCollisionType.World;

        // Only render once
        cam.enabled = false;
    }
}
