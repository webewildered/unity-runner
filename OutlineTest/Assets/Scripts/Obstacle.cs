using UnityEngine;
using System.Collections;

public class Obstacle : MonoBehaviour
{
    public SampledBlock Block;
    public Material MeshMaterial;
    static int[] blockTriangles;

    float timer = -1.0f;
    Vector3 explosionPosition;
    float explosionRadius;

    static Obstacle()
    {
        blockTriangles = new int[36];
        for (int i = 0; i < 36; i++)
        {
            blockTriangles[i] = i;
        }
    }

    // Use this for initialization
    void Start() {
    }

    // Update is called once per frame
    void Update()
    {
        if (timer > 0.0f)
        {
            if (timer < Time.deltaTime)
            {
                explode();
            }

            timer -= Time.deltaTime;
        }
    }

    Vector3 interpolate(Vector3 a, Vector3 b, float t)
    {
        return b * t + a * (1.0f - t);
    }

    Mesh makeBox(Vector3[] v)
    {
        Vector3[] vertices = new Vector3[36];
        vertices[0] = v[0];
        vertices[1] = v[4];
        vertices[2] = v[1];
        vertices[3] = v[4];
        vertices[4] = v[5];
        vertices[5] = v[1];
        vertices[6] = v[1];
        vertices[7] = v[5];
        vertices[8] = v[3];
        vertices[9] = v[5];
        vertices[10] = v[7];
        vertices[11] = v[3];
        vertices[12] = v[3];
        vertices[13] = v[7];
        vertices[14] = v[2];
        vertices[15] = v[7];
        vertices[16] = v[6];
        vertices[17] = v[2];
        vertices[18] = v[2];
        vertices[19] = v[6];
        vertices[20] = v[0];
        vertices[21] = v[6];
        vertices[22] = v[4];
        vertices[23] = v[0];
        vertices[24] = v[4];
        vertices[25] = v[6];
        vertices[26] = v[5];
        vertices[27] = v[6];
        vertices[28] = v[7];
        vertices[29] = v[5];
        vertices[30] = v[2];
        vertices[31] = v[0];
        vertices[32] = v[3];
        vertices[33] = v[0];
        vertices[34] = v[1];
        vertices[35] = v[3];

        // compute normals
        Vector3[] normals = new Vector3[36];
        for (int i = 0; i < 6; i++)
        {
            int j = i * 6;
            Vector3 e0 = vertices[j + 1] - vertices[j];
            Vector3 e1 = vertices[j + 2] - vertices[j];
            Vector3 n = Vector3.Cross(e0, e1);

            normals[j] = n;
            normals[j + 1] = n;
            normals[j + 2] = n;
            normals[j + 3] = n;
            normals[j + 4] = n;
            normals[j + 5] = n;
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = blockTriangles;

        return mesh;
    }

    void makeChunk(Mesh mesh, Vector3 position)
    {
        GameObject chunk = new GameObject();
        chunk.transform.parent = FloorGenerator.Instance.transform;
        chunk.transform.localPosition = position;
        chunk.layer = 9; // Debris layer

        MeshFilter filter = chunk.AddComponent<MeshFilter>();
        filter.mesh = mesh;

        MeshRenderer renderer = chunk.AddComponent<MeshRenderer>();
        renderer.material = MeshMaterial;

        MeshCollider meshCollider = chunk.AddComponent<MeshCollider>();
        meshCollider.convex = true;
        meshCollider.sharedMesh = mesh;

        Rigidbody rb = chunk.AddComponent<Rigidbody>();
        rb.mass = 1.0f;

        const float explosionForce = 1000.0f;
        const float explosionRadiusFactor = 2.0f;
        rb.AddExplosionForce(explosionForce, explosionPosition, explosionRadius * explosionRadiusFactor);
        //rb.AddForce(new Vector3(0, -explosionForce / 300.0f, 0), ForceMode.Impulse);
    }

    public void Bomb(Vector3 position, float radius)
    {
        // if behind the camera, doesn't matter, don't do anything
        Camera camera = Camera.main;
        Bounds bounds = GetComponent<Collider>().bounds;
        Vector3 cameraDiff = camera.transform.position - bounds.center;
        float ext = Mathf.Abs(bounds.extents.x * camera.transform.forward.x) + Mathf.Abs(bounds.extents.z * camera.transform.forward.z);
        if (Vector3.Dot(cameraDiff, camera.transform.forward) > ext - camera.nearClipPlane)
        {
            return;
        }

        explosionPosition = position;
        explosionRadius = radius;
        const float timeFactor = 1.0f / 3.0f; // Time for explosion to reach end of radius
        timer = timeFactor * (explosionPosition - bounds.center).magnitude / radius - 0.01f;
        if (timer < 0.0f)
        {
            explode();
        }
    }

    void explode()
    {

        timer = 0.0f;
        const float chunkSize = 1.0f;

        // Break the block into chunks
        Vector3 dx0 = Block.Samples[0].Right - Block.Samples[0].Left;
        float width = dx0.magnitude;
        int chunksWide = Mathf.CeilToInt(width / chunkSize);
        int chunksHigh = Mathf.CeilToInt(Block.Height / chunkSize);
        float chunkWidth = width / chunksWide;
        float chunkHeight = Block.Height / chunksHigh;

        dx0 *= chunkWidth / width;

        Vector3 dx1 = Block.Samples[1].Right - Block.Samples[1].Left;
        dx1 *= chunkWidth / dx1.magnitude;

        Vector3 dy = new Vector3(0.0f, chunkHeight, 0.0f);
        Vector3[] v = new Vector3[8];

        SampledBlock.Sample s0 = Block.Samples[0];
        SampledBlock.Sample s1 = Block.Samples[1];

        // Front face
        for (int i = 0; i < chunksWide; i++)
        {
            v[0] = s0.Left + i * dx0;
            v[1] = s0.Left + (i + 1) * dx0;
            v[2] = s1.Left + i * dx1;
            v[3] = s1.Left + (i + 1) * dx1;
            v[4] = v[0] + dy;
            v[5] = v[1] + dy;
            v[6] = v[2] + dy;
            v[7] = v[3] + dy;

            Mesh mesh = makeBox(v);

            for (int j = 0; j < chunksHigh; j++)
            {
                makeChunk(mesh, j * dy);
            }
        }

        // Side face, only do the face closest to the player to save effort, won't notice the other side missing
        float t0 = 0;
        float t1 = 0;
        int i0 = 0; // for building top face to not duplicate side chunks
        int i1 = chunksWide;
        bool buildSide = false;
        Camera camera = Camera.main;
        if (Vector3.Dot(s0.Right - camera.transform.position, camera.transform.right) < 0.0f)
        {
            buildSide = true;
            t0 = (float)(chunksWide - 1) / chunksWide;
            t1 = 1.0f;
            i1--;
        }
        else if (Vector3.Dot(s0.Left - camera.transform.position, camera.transform.right) > 0.0f)
        {
            buildSide = true;
            t0 = 0.0f;
            t1 = 1.0f / chunksWide;
            i0++;
        }

        if (buildSide)
        {
            for (int i = 1; i < Block.Samples.Count - 1; i++)
            {
                s0 = Block.Samples[i];
                s1 = Block.Samples[i + 1];

                v[0] = interpolate(s0.Left, s0.Right, t0);
                v[1] = interpolate(s0.Left, s0.Right, t1);
                v[2] = interpolate(s1.Left, s1.Right, t0);
                v[3] = interpolate(s1.Left, s1.Right, t1);
                v[4] = v[0] + dy;
                v[5] = v[1] + dy;
                v[6] = v[2] + dy;
                v[7] = v[3] + dy;

                Mesh mesh = makeBox(v);

                for (int j = 0; j < chunksHigh; j++)
                {
                    makeChunk(mesh, j * dy);
                }
            }
        }

        // Top face if visible
        if (chunksHigh > 1 && Block.Samples.Count > 2 && Block.Height < camera.transform.position.y)
        {
            Vector3 topPos = new Vector3(0, Block.Height - chunkHeight, 0);
            for (int i = 1; i < Block.Samples.Count - 1; i++)
            {
                s0 = Block.Samples[i];
                s1 = Block.Samples[i + 1];

                for (int j = i0; j < i1; j++)
                {
                    t0 = (float)j / chunksWide;
                    t1 = (float)(j + 1) / chunksWide;

                    v[0] = interpolate(s0.Left, s0.Right, t0);
                    v[1] = interpolate(s0.Left, s0.Right, t1);
                    v[2] = interpolate(s1.Left, s1.Right, t0);
                    v[3] = interpolate(s1.Left, s1.Right, t1);
                    v[4] = v[0] + dy;
                    v[5] = v[1] + dy;
                    v[6] = v[2] + dy;
                    v[7] = v[3] + dy;

                    Mesh mesh = makeBox(v);
                    makeChunk(mesh, topPos);
                }
            }
        }

        // Remove self
        GameObject.Destroy(gameObject);
    }
}
