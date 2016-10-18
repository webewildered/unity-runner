using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class FloorGenerator : MonoBehaviour
{
    public float Res = 0.5f;
    public float MaxSpeed = 10.0f;
    public float PeriodScale = 0.01f;

    Vector3 up = new Vector3(0f, 1f, 0f);

    struct Block : IComparable<Block>
    {
        public float Start;
        public float Duration;
        public float Left;
        public float Right;
        public float Height;

        public int CompareTo(Block other)
        {
            if (Start < other.Start)
            {
                return -1;
            }
            if (Start == other.Start)
            {
                return 0;
            }
            return 1;
        }

    }

    abstract class Curve
    {
        // Returns derivative of the curve's angle with respect to time
        public abstract void Sample(float t, out float deltaAngle, out float width);
        public List<Block> Blocks; // Must be sorted by time

        public Curve()
        {
            Blocks = new List<Block>();
        }
    }

    class MultiCurve : Curve
    {
        public struct Piece
        {
            public Curve Curve;
            public float Duration;
            public float Start; // Computed automatically
        }

        Piece[] pieces;
        Piece lastPiece;

        public MultiCurve(Piece[] pieces)
        {
            this.pieces = pieces;
            float t = 0;
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i].Start = t;
                t += pieces[i].Duration;
            }
            lastPiece = pieces[0];
        }

        public override void Sample(float t, out float deltaAngle, out float width)
        {
            if (t < lastPiece.Start || t > lastPiece.Start + lastPiece.Duration)
            {
                for (int i = 0; i < pieces.Length; i++)
                {
                    lastPiece = pieces[i];
                    if (t < lastPiece.Start + lastPiece.Duration)
                    {
                        break;
                    }
                }
            }

            lastPiece.Curve.Sample(t - lastPiece.Start, out deltaAngle, out width);
        }
    }

    class StraightCurve : Curve
    {
        float width;

        public StraightCurve(float width)
        {
            this.width = width;
        }

        public override void Sample(float t, out float deltaAngle, out float width)
        {
            deltaAngle = 0.0f;
            width = this.width;
        }
    }

    class WiggleCurve : Curve
    {
        float width;

        public WiggleCurve(float width)
        {
            this.width = width;
        }

        public override void Sample(float t, out float deltaAngle, out float width)
        {
            // TODO: params
            const float invPeriodA = 0.1f;//(2.0f + Random.value) * PeriodScale;
            const float invPeriodB = 0.032f;//(1.0f + Random.value) * PeriodScale;
            const float magnitudeA = 0.1f;//(2.0f + Random.value) * 0.05f;
            const float magnitudeB = 0.05f;//(2.0f + Random.value) * 0.1f;

            deltaAngle = (Mathf.Sin(t * invPeriodA) * magnitudeA + Mathf.Sin(t * invPeriodB) * magnitudeB);
            width = this.width;
        }
    }

    class WaveCurve : Curve
    {
        float width;

        public WaveCurve(float width)
        {
            this.width = width;
        }

        public override void Sample(float t, out float deltaAngle, out float width)
        {
            // TODO: params
            const float invPeriodA = 0.1f;//(2.0f + Random.value) * PeriodScale;
            const float invPeriodB = 0.005f;//(1.0f + Random.value) * PeriodScale;
            const float magnitudeA = 0.075f;//(2.0f + Random.value) * 0.05f;
            const float magnitudeB = 0.25f;//(2.0f + Random.value) * 0.1f;
            //const float magnitudeB = 0.05f;//(2.0f + Random.value) * 0.1f;

            //return Mathf.Cos(t * invPeriodA) * (magnitudeA + (Mathf.Sin(t * invPeriodB) + 1.0f) * magnitudeB);
            float invPeriodFactor = 1.0f - (1.0f - Mathf.Cos(t * invPeriodB)) * 0.5f * magnitudeB;
            deltaAngle = Mathf.Cos(t * invPeriodA * invPeriodFactor) * magnitudeA;
            width = this.width;
        }
    }

    void appendPlane(float halfSize, ref List<Vector3> vertices, ref List<Vector3> normals, ref List<int> triangles)
    {
        int v0 = vertices.Count;

        for (int i = 0; i < 4; i++)
        {
            int x = (i % 2) * 2 - 1;
            int z = (i / 2) * 2 - 1;
            vertices.Add(new Vector3(x * halfSize, 0.0f, z * halfSize));
            normals.Add(new Vector3(0, 1, 0));
        }

        triangles.Add(v0 + 2);
        triangles.Add(v0 + 1);
        triangles.Add(v0 + 0);
        triangles.Add(v0 + 3);
        triangles.Add(v0 + 1);
        triangles.Add(v0 + 2);
    }

    void finalizeCurve(Curve curve)
    {
        curve.Blocks.Sort();

        Block endBlock = new Block();
        endBlock.Start = float.MaxValue;
        curve.Blocks.Add(endBlock);
    }    // pass vertices in CW order

    void appendFace(int v0, int v1, int v2, int v3, ref List<Vector3> vertices, ref List<Vector3> normals, ref List<int> triangles)
    {
        Vector3 e0 = vertices[v1] - vertices[v0];
        Vector3 e1 = vertices[v2] - vertices[v0];
        Vector3 normal = Vector3.Cross(e0, e1);

        normals[v0] = normal;
        normals[v1] = normal;
        normals[v2] = normal;
        normals[v3] = normal;

        triangles.Add(v0);
        triangles.Add(v1);
        triangles.Add(v2);
        triangles.Add(v0);
        triangles.Add(v2);
        triangles.Add(v3);
    }

    void appendBlockVertices(Block block, Vector3 center, Vector3 arm, ref List<Vector3> vertices, ref List<Vector3> normals)
    {
        Vector3 left = center + (block.Left * 2.0f - 1.0f) * arm;
        Vector3 right = center + (block.Right * 2.0f - 1.0f) * arm;
        Vector3 vert = up * block.Height;
        vertices.Add(left);
        vertices.Add(left + vert);
        vertices.Add(left + vert);
        vertices.Add(right + vert);
        vertices.Add(right + vert);
        vertices.Add(right);

        normals.Add(Vector3.zero);
        normals.Add(Vector3.zero);
        normals.Add(Vector3.zero);
        normals.Add(Vector3.zero);
        normals.Add(Vector3.zero);
        normals.Add(Vector3.zero);
    }

    void appendCurve(Curve curve, float length, ref List<Vector3> vertices, ref List<Vector3> normals, ref List<int> triangles)
    {
        // Build a random mesh
        //Random.InitState((int)System.DateTime.Now.Ticks);

        Vector3 center = Vector3.zero;
        Vector3 lastCenter = Vector3.zero;
        Vector3 lastArm = Vector3.zero;
        float t = 0.0f;
        float angle = 0.0f;

        int numSteps = (int)(length / Res) + 1;
        int vertexIndex = vertices.Count;
        int lastFloorVertexIndex = 0;
        int minBlockIndex = 0;
        for (int i = 0; i < numSteps; i++)
        {
            // Calculate current angle
            float deltaAngle, width;
            curve.Sample(t, out deltaAngle, out width);
            angle += deltaAngle * Res;

            // Add vertices at the current position
            float cosAngle = Mathf.Cos(angle);
            float sinAngle = Mathf.Sin(angle);
            Vector3 arm = new Vector3(cosAngle, 0.0f, -sinAngle) * width * 0.5f;
            vertices.Add(center - arm);
            vertices.Add(center + arm);
            normals.Add(up);
            normals.Add(up);

            // Add triangles
            if (i > 0)
            {
                triangles.Add(vertexIndex);
                triangles.Add(lastFloorVertexIndex + 1);
                triangles.Add(lastFloorVertexIndex);
                triangles.Add(lastFloorVertexIndex + 1);
                triangles.Add(vertexIndex);
                triangles.Add(vertexIndex + 1);
            }
            lastFloorVertexIndex = vertexIndex;
            vertexIndex += 2;

            // Add blocks
            int blockIndex = minBlockIndex;
            minBlockIndex = curve.Blocks.Count - 1;
            while (true)
            {
                Block block = curve.Blocks[blockIndex];
                if (block.Start > t)
                {
                    minBlockIndex = Math.Min(minBlockIndex, blockIndex);
                    break;
                }
                if (block.Start + block.Duration < t)
                {
                    blockIndex++;
                    continue;
                }
                minBlockIndex = Math.Min(minBlockIndex, blockIndex);
                blockIndex++;

                // Add block vertices
                appendBlockVertices(block, center, arm, ref vertices, ref normals);
                if (t - block.Start < Res)
                {
                    // Create the front face
                    appendFace(vertexIndex, vertexIndex + 1, vertexIndex + 4, vertexIndex + 5, ref vertices, ref normals, ref triangles);
                    vertexIndex += 6;
                }
                else
                {
                    // Create the side faces
                    appendBlockVertices(block, lastCenter, lastArm, ref vertices, ref normals);
                    appendFace(vertexIndex + 0, vertexIndex + 1, vertexIndex + 7, vertexIndex + 6, ref vertices, ref normals, ref triangles);
                    appendFace(vertexIndex + 2, vertexIndex + 3, vertexIndex + 9, vertexIndex + 8, ref vertices, ref normals, ref triangles);
                    appendFace(vertexIndex + 4, vertexIndex + 5, vertexIndex + 11, vertexIndex + 10, ref vertices, ref normals, ref triangles);
                    vertexIndex += 12;
                }
            }

            // Move to next position
            lastCenter = center;
            lastArm = arm;
            Vector3 forward = new Vector4(sinAngle, 0.0f, cosAngle);
            center += forward * Res;
            t += Res;
        }
    }

    void buildCurve(Curve curve, float length)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();
        appendCurve(curve, length, ref vertices, ref normals, ref triangles);
        setMesh(vertices.ToArray(), normals.ToArray(), triangles.ToArray());
    }

    void clear()
    {
        IEnumerator childEnumerator = transform.GetEnumerator();
        while (true)
        {
            if (childEnumerator.MoveNext() == false)
            {
                break;
            }
            Transform child = (Transform)childEnumerator.Current;
            GameObject.DestroyImmediate(child.gameObject);
            childEnumerator.Reset();
        }
    }

	public void Rebuild ()
    {
        clear();

        buildCurve(new WiggleCurve(5.0f), 1000.0f);
    }

    public void RebuildWave()
    {
        clear();

        MultiCurve.Piece[] pieces = new MultiCurve.Piece[2];
        pieces[0].Curve = new StraightCurve(5.0f);
        pieces[0].Duration = 10.0f;
        pieces[1].Curve = new WaveCurve(5.0f);
        pieces[1].Duration = 10000.0f;
        buildCurve(new MultiCurve(pieces), 1000.0f);
    }

    public void RebuildOpen()
    {
        clear();

        const float halfSize = 500.0f;

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();
        appendPlane(halfSize, ref vertices, ref normals, ref triangles);

        setMesh(vertices.ToArray(), normals.ToArray(), triangles.ToArray());
    }

    public void RebuildField()
    {
        clear();

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();

        const float halfWidth = 10.0f;
        const float length = 1000.0f;
        Curve curve = new StraightCurve(2.0f * halfWidth);

        /*
        Block block = new Block();
        block.Start = 1.0f;
        block.Duration = 1.0f;
        block.Height = 1.0f;
        block.Left = 0.25f;
        block.Right = 0.75f;
        curve.Blocks.Add(block);
        */

        // Add some obstacles
        Rng rng = new Rng(123456);
        int numSteps = (int)(length / Res);
        for (int i = 0; i < 100; i++)
        {
            Block block = new Block();
            block.Start = rng.Range(0, numSteps) * Res;
            block.Duration = rng.Range(1, 3) * Res;
            block.Height = rng.Range(1.0f, 5.0f);
            block.Left = rng.Range(0.0f, 0.9f);
            block.Right = Mathf.Min(block.Left + rng.Range(0.1f, 0.2f), 1.0f);
            curve.Blocks.Add(block);
        }
        finalizeCurve(curve);

        appendCurve(curve, length, ref vertices, ref normals, ref triangles);
        setMesh(vertices.ToArray(), normals.ToArray(), triangles.ToArray());

        // Add some obstacles
        /*
        GameObject boxPrefab = Resources.Load<GameObject>("Obstacles/BoxPrefab");
        Rng rng = new Rng(123456);
        for (int i = 0; i < 200; i++)
        {
            //spawn object
            GameObject box = Instantiate<GameObject>(boxPrefab);
            box.transform.parent = transform;
            box.transform.position = new Vector3(rng.Range(-halfWidth, halfWidth), 0.0f, rng.Range(20.0f, length));
            box.transform.localScale = new Vector3(1.0f, rng.Range(1.0f, 10.0f), 1.0f);
        }
        */
    }

    void setMesh(Vector3[] vertices, Vector3[] normals, int[] triangles)
    {
        Mesh mesh = new Mesh();

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;

        GetComponent<MeshFilter>().mesh = mesh;
        GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    void Start()
    {

    }

    void Update ()
    {
	
	}
}
