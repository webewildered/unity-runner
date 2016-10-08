using UnityEngine;
using System.Collections;

public class FloorGenerator : MonoBehaviour
{
    public float Length = 100.0f;
    public float Width = 5.0f;
    public float Res = 0.5f;
    public float MaxSpeed = 10.0f;
    public float PeriodScale = 0.01f;

	public void Rebuild ()
    {
        // Build a random mesh
        Random.InitState((int)System.DateTime.Now.Ticks);
        Mesh mesh = new Mesh();
        Vector3 up = new Vector3(0f, 1f, 0f);

        float invPeriodA = 0.1f;//(2.0f + Random.value) * PeriodScale;
        float invPeriodB = 0.032f;//(1.0f + Random.value) * PeriodScale;
        float magnitudeA = 0.1f;//(2.0f + Random.value) * 0.05f;
        float magnitudeB = 0.05f;//(2.0f + Random.value) * 0.1f;

        float halfWidth = Width / 2.0f;

        Vector3 center = Vector3.zero;
        float t = 0.0f;
        float angle = 0.0f;

        int numSteps = (int)(Length / Res) + 1;
        int numVertices = numSteps * 2;
        int numTriangles = numVertices - 2;
        Vector3[] normals = new Vector3[numVertices];
        Vector3[] vertices = new Vector3[numVertices];
        int[] triangles = new int[numTriangles * 3];
        int vertexIndex = 0;
        int triangleIndex = 0;
        for (int i = 0; i < numSteps; i++)
        {
            // Calculate current angle
            float deltaAngle = (Mathf.Sin(t * invPeriodA) * magnitudeA + Mathf.Sin(t * invPeriodB) * magnitudeB) * Res;
            angle += deltaAngle;
            t += Res;

            // Add vertices at the current position
            float cosAngle = Mathf.Cos(angle);
            float sinAngle = Mathf.Sin(angle);
            Vector3 arm = new Vector3(cosAngle, 0.0f, -sinAngle) * halfWidth;
            vertices[vertexIndex] = center - arm;
            vertices[vertexIndex + 1] = center + arm;
            normals[vertexIndex] = up;
            normals[vertexIndex + 1] = up;
            vertexIndex += 2;

            // Add triangles
            if (i > 0)
            {
                triangles[triangleIndex] = vertexIndex - 2;
                triangles[triangleIndex + 1] = vertexIndex - 3;
                triangles[triangleIndex + 2] = vertexIndex - 4;
                triangles[triangleIndex + 3] = vertexIndex - 3;
                triangles[triangleIndex + 4] = vertexIndex - 2;
                triangles[triangleIndex + 5] = vertexIndex - 1;
                triangleIndex += 6;
            }

            // Move to next position
            Vector3 forward = new Vector4(sinAngle, 0.0f, cosAngle);
            center += forward * Res;
        }

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;

        GetComponent< MeshFilter >().mesh = mesh;
        GetComponent<MeshCollider>().sharedMesh = mesh;
	}

    void Start()
    {

    }

    void Update ()
    {
	
	}
}
