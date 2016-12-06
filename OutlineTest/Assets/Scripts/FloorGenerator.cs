using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

public class FloorGenerator : MonoBehaviour
{
    enum State
    {
        Play,
        PostPlay,
        TransitionOut,
        TransitionIn
    }

    public static FloorGenerator Instance
    {
        get { return instance; }
    }
    static FloorGenerator instance;

    State state = State.Play;

    const float baseWidth = 40.0f;
    const float baseHalf = baseWidth * 0.5f;
    const float minWidth = 10.0f;

    public float Res = 1.0f;
    public bool BuildObstacles = true;
    public Material MeshMaterial;
    public Material BestMaterial;

    bool initializing;

    Rng rng;

    // Curve state
    Vector3 center = Vector3.zero;
    Vector3 lastCenter = Vector3.zero;
    Vector3 lastArm = Vector3.zero;
    float lastLeft;
    float lastRight;
    float t = 0.0f;
    float angle = 0.0f;
    float leftV = 0.0f;
    float rightV = 0.0f;
    int specialTimer = 0; // Number of sections until the next special

    const float powerupSpace = 100.0f;
    float powerupPosition = 0.0f;

    // When true, use whatever levels were created in the editor
    public bool EditorMode = false;

    int nextChallenge;
    public int BaseChallenge = 0; // for debugging

    // Progress tracking
    public Queue<Vector4> Planes;

    // When crossed, remove an old section and add a new one
    public Queue<Vector4> TriggerPlanes;
    Queue<GameObject> sections;

    public GameObject PowerupPrefab;

    class Block : IComparable<Block>
    {
        public float Start;
        public float Duration;
        public float Left;
        public float Right;
        public float Height;

        public List<Vector3> Vertices;
        public List<Vector3> Normals;
        public List<int> Triangles;

        public SampledBlock Sampled;

        public Block()
        {
            Start = 0;
            Duration = 0;
            Left = 0;
            Right = 0;
            Height = 0;

            Vertices = new List<Vector3>();
            Normals = new List<Vector3>();
            Triangles = new List<int>();

            Sampled = new SampledBlock();
        }

        public int CompareTo(Block other)
        {
            if (Start < other.Start)
            {
                return -1;
            }
            if (Start == other.Start)
            {
                if (Left < other.Left)
                {
                    return -1;
                }
                return 0;
            }
            return 1;
        }
    }

    class Powerup
    {
        public float Time;
        public float Position;
        public Vector3 SampledPosition;

        public Powerup(float time, float position)
        {
            Time = time;
            Position = position;
            SampledPosition = Vector3.zero;
        }
    }

    abstract class Curve
    {
        // Returns derivative of the curve's angle with respect to time
        public abstract void Sample(float t, out float deltaAngle, out float left, out float right);
        public List<Block> Blocks; // Must be sorted by time
        public List<Powerup> Powerups; // x is time, y is horizontal position 0 to 1
        public float Length;

        public Curve()
        {
            Blocks = new List<Block>();
            Powerups = new List<Powerup>();
        }
    }

    class WidthModifier : Curve
    {
        public Curve BaseCurve;
        public List<Vector3> Changes; // x is time, y and z are left and right width multipliers

        public WidthModifier(Curve baseCurve)
        {
            BaseCurve = baseCurve;
            Blocks = baseCurve.Blocks;
            Powerups = baseCurve.Powerups;

            Changes = new List<Vector3>();
            Changes.Add(new Vector3(0, 1, 1));
        }

        public override void Sample(float t, out float deltaAngle, out float left, out float right)
        {
            BaseCurve.Sample(t, out deltaAngle, out left, out right);
            int lastChange;
            for (lastChange = 1; lastChange < Changes.Count; lastChange++)
            {
                if (Changes[lastChange].x > t)
                {
                    break;
                }
            }
            lastChange--;
            left *= Changes[lastChange].y;
            right *= Changes[lastChange].z;
        }
    }

    class ConstantCurve : Curve
    {
        float deltaAngle;
        float halfWidth;

        public ConstantCurve(float deltaAngle, float width)
        {
            this.deltaAngle = deltaAngle;
            halfWidth = width * 0.5f;
        }

        public override void Sample(float t, out float deltaAngle, out float left, out float right)
        {
            deltaAngle = this.deltaAngle;
            left = -halfWidth;
            right = halfWidth;
        }
    }

    class SinCurve : Curve
    {
        float start;
        float invPeriod;
        float magnitude;
        float halfWidth;

        public SinCurve(float start, float length, float magnitude, float width)
        {
            this.start = start;
            invPeriod = Mathf.PI * 2.0f / length;
            this.magnitude = magnitude;
            halfWidth = width * 0.5f;
        }

        public override void Sample(float t, out float deltaAngle, out float left, out float right)
        {
            t -= start;
            deltaAngle = Mathf.Sin(t * invPeriod) * magnitude;
            left = -halfWidth;
            right = halfWidth;
        }
    }

    class HourglassCurve : Curve
    {
        float start;
        float minHalfWidth;
        float maxHalfWidth;
        float invPeriod;

        public HourglassCurve(float start, float period, float minWidth, float maxWidth)
        {
            this.start = start;
            invPeriod = 0.5f / period;
            minHalfWidth = minWidth / 2.0f;
            maxHalfWidth = maxWidth / 2.0f;
        }

        public override void Sample(float t, out float deltaAngle, out float left, out float right)
        {
            t -= start;
            float halfWidth = minHalfWidth + Mathf.Abs(Mathf.Cos(t * invPeriod * 2 * Mathf.PI)) * (maxHalfWidth - minHalfWidth);
            left = -halfWidth;
            right = halfWidth;
            deltaAngle = 0.0f;
        }
    }

    class WiggleCurve : Curve
    {
        float start;
        float halfWidth;

        float invPeriodA;
        float invPeriodB;
        float magnitudeA;
        float magnitudeB;

        public WiggleCurve(float start, float length, float width, Rng rng)
        {
            this.start = start;
            Length = length;
            halfWidth = width * 0.5f;

            invPeriodA = rng.Range(0.1f, 0.2f);
            invPeriodB = rng.Range(0.05f, 0.07f);
            magnitudeA = rng.Range(0.04f, 0.07f);
            magnitudeB = rng.Range(0.015f, 0.03f);
        }

        public override void Sample(float t, out float deltaAngle, out float left, out float right)
        {
            t -= start;
            deltaAngle = (Mathf.Sin(t * invPeriodA) * magnitudeA + Mathf.Sin(t * invPeriodB) * magnitudeB);
            left = -halfWidth;
            right = halfWidth;
        }
    }

    class WaveCurve : Curve
    {
        float start;
        float halfWidth;

        public WaveCurve(float start, float length, float width)
        {
            this.start = start;
            Length = length;
            halfWidth = width * 0.5f;
        }

        public override void Sample(float t, out float deltaAngle, out float left, out float right)
        {
            t -= start;
            const float count = 5.0f;
            const float mag = 0.10f;

            float f = t / Length;           // 0 to 1
            float x = (f - 0.5f) * 2.0f;    // -1 to 1
            float p = 1.3f;
            float x2 = (Mathf.Pow(Mathf.Abs(x), p) * Mathf.Sign(x) + 1.0f) / 2.0f; // 0 to 1
            deltaAngle = Mathf.Sin(x2 * 2 * Mathf.PI * count) * mag;
            left = -halfWidth;
            right = halfWidth;
        }
    }

    class ZigZagCurve : Curve
    {
        public struct Turn
        {
            public float Time;
            public float Duration;
            public float Direction;

            public Turn(float time, float duration, float direction)
            {
                Time = time;
                Duration = duration;
                Direction = direction;
            }
        }

        public List<Turn> turns;

        float res;
        float start;
        float maxAngle;
        float halfWidth;
        int lastTurnIndex;

        public ZigZagCurve(float start, float res, float width)
        {
            this.start = start;
            this.res = res;
            maxAngle = 0.9f * Mathf.Asin(2 * res / width); // TODO - maybe could have problems when another curve varies the width
            halfWidth = width * 0.5f;
            turns = new List<Turn>();
        }
        
        public ZigZagCurve(float start, float length, int approxCount, float res, float width, Rng rng) :
            this(start, res, width)
        {
            lastTurnIndex = 0;

            float t = 0;
            float averageDistance = length / approxCount;
            while (t < length)
            {
                float distance = rng.Range(averageDistance * 0.5f, averageDistance * 1.5f);
                float duration = rng.Range(res * 5, res * 15);
                t += distance;
                Turn turn = new Turn(t, duration, rng.PlusOrMinusOne());
                t += duration;
                turns.Add(turn);
            }
        }

        public override void Sample(float t, out float deltaAngle, out float left, out float right)
        {
            t -= start;
            deltaAngle = 0.0f;
            left = -halfWidth;
            right = halfWidth;

            if (turns[lastTurnIndex].Time > t)
            {
                lastTurnIndex = 0;
            }

            while (true)
            {
                Turn turn = turns[lastTurnIndex];
                if (turn.Time > t)
                {
                    deltaAngle = 0.0f;
                    break;
                }
                if (turn.Time + turn.Duration < t)
                {
                    if (lastTurnIndex == turns.Count - 1)
                    {
                        break;
                    }
                    lastTurnIndex++;
                    continue;
                }
                deltaAngle = maxAngle * turn.Direction;
                break;
            }
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

    void appendFace(Block block, int v0, int v1, int v2, int v3)
    {
        Vector3 e0 = block.Vertices[v1] - block.Vertices[v0];
        Vector3 e1 = block.Vertices[v2] - block.Vertices[v0];
        Vector3 normal = Vector3.Cross(e0, e1);

        block.Normals[v0] = normal;
        block.Normals[v1] = normal;
        block.Normals[v2] = normal;
        block.Normals[v3] = normal;

        block.Triangles.Add(v0);
        block.Triangles.Add(v1);
        block.Triangles.Add(v2);
        block.Triangles.Add(v0);
        block.Triangles.Add(v2);
        block.Triangles.Add(v3);
    }

    Vector3 curvePos(Vector3 center, Vector3 arm, float x)
    {
        return center + (x * 2.0f - 1.0f) * arm;
    }

    Vector3 blockLeft(Block block, Vector3 center, Vector3 arm)
    {
        return curvePos(center, arm, block.Left);
    }

    Vector3 blockRight(Block block, Vector3 center, Vector3 arm)
    {
        return curvePos(center, arm, block.Right);
    }

    int appendBlockVertices(Block block, Vector3 left, Vector3 right)
    {
        int vertexIndex = block.Vertices.Count;

        Vector3 vert = Util.Up * block.Height;
        block.Vertices.Add(left);
        block.Vertices.Add(left + vert);
        block.Vertices.Add(left + vert);
        block.Vertices.Add(right + vert);
        block.Vertices.Add(right + vert);
        block.Vertices.Add(right);

        block.Normals.Add(Vector3.zero);
        block.Normals.Add(Vector3.zero);
        block.Normals.Add(Vector3.zero);
        block.Normals.Add(Vector3.zero);
        block.Normals.Add(Vector3.zero);
        block.Normals.Add(Vector3.zero);

        return vertexIndex;
    }

    void gainV(ref float pos, ref float v, float target, float gain)
    {
        float targetV = (target - pos) * gain;
        v = targetV * gain + v * (1.0f - gain);
        pos = pos + v;
    }

    void appendCurve(Curve curve)
    {
        // shift to keep stuff close to the origin in local space
        // this is awful code but the performance probably isn't noticeable?
        if (!initializing) // don't shift during initial add, change in transform causes problems with transition
        {
            Vector3 centerWorld = transform.rotation * center;
            foreach (GameObject sectionObj in sections)
            {
                sectionObj.transform.position -= centerWorld;
            }

            Queue<Vector4> newPlanes = new Queue<Vector4>();
            foreach (Vector4 plane in Planes)
            {
                Vector3 p = new Vector3(plane.x, plane.y, plane.z);
                newPlanes.Enqueue(new Vector4(plane.x, plane.y, plane.z, plane.w - Vector3.Dot(p, center)));
            }
            Planes = newPlanes;

            Queue<Vector4> newTriggerPlanes = new Queue<Vector4>();
            foreach (Vector4 plane in TriggerPlanes)
            {
                Vector3 p = new Vector3(plane.x, plane.y, plane.z);
                newTriggerPlanes.Enqueue(new Vector4(plane.x, plane.y, plane.z, plane.w - Vector3.Dot(p, center)));
            }
            TriggerPlanes = newTriggerPlanes;

            lastCenter -= center;
            transform.position += centerWorld;
            center = Vector3.zero;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();

        List<Vector3> best = new List<Vector3>();

        // Add vertices connecting to the previous section
        vertices.Add(lastCenter - lastArm);
        vertices.Add(lastCenter + lastArm);
        normals.Add(Util.Up);
        normals.Add(Util.Up);

        int numSteps = (int)(curve.Length / Res) + 1;
        int vertexIndex = vertices.Count;
        int lastFloorVertexIndex = 0;
        int minBlockIndex = 0;
        int minPowerupIndex = 0;
        for (int i = 0; i < numSteps; i++)
        {
            // Calculate current angle
            float deltaAngle, left, right;
            curve.Sample(t, out deltaAngle, out left, out right);
            angle += deltaAngle * Res;

            // Calculate side direction
            float cosAngle = Mathf.Cos(angle);
            float sinAngle = Mathf.Sin(angle);
            Vector3 direction = new Vector3(cosAngle, 0.0f, -sinAngle);

            // TODO - maybe add some smoothing to change in width?
            leftV = left - lastLeft;
            lastLeft = left;
            rightV = right - lastRight;
            lastRight = right;

            center += direction * (rightV + leftV) * 0.5f;
            float width = lastRight - lastLeft;

            // Add vertices at the current position
            Vector3 arm = direction * width * 0.5f;
            vertices.Add(center - arm);
            vertices.Add(center + arm);
            normals.Add(Util.Up);
            normals.Add(Util.Up);

            // Add triangles
            triangles.Add(vertexIndex);
            triangles.Add(lastFloorVertexIndex + 1);
            triangles.Add(lastFloorVertexIndex);
            triangles.Add(lastFloorVertexIndex + 1);
            triangles.Add(vertexIndex);
            triangles.Add(vertexIndex + 1);
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

                SampledBlock.Sample sample = new SampledBlock.Sample();
                sample.Left = blockLeft(block, center, arm);
                sample.Right = blockRight(block, center, arm);
                block.Sampled.Samples.Add(sample);

                // Add block vertices
                int blockVertexIndex = appendBlockVertices(block, sample.Left, sample.Right);
                if (t - block.Start < Res)
                {
                    // Create the front face
                    appendFace(block, blockVertexIndex, blockVertexIndex + 1, blockVertexIndex + 4, blockVertexIndex + 5);
                }
                else
                {
                    // Create the side faces
                    appendBlockVertices(block, blockLeft(block, lastCenter, lastArm), blockRight(block, lastCenter, lastArm));
                    appendFace(block, blockVertexIndex + 0, blockVertexIndex + 1, blockVertexIndex + 7, blockVertexIndex + 6);
                    appendFace(block, blockVertexIndex + 2, blockVertexIndex + 3, blockVertexIndex + 9, blockVertexIndex + 8);
                    appendFace(block, blockVertexIndex + 4, blockVertexIndex + 5, blockVertexIndex + 11, blockVertexIndex + 10);
                }
            }

            // Add powerups
            while (minPowerupIndex < curve.Powerups.Count)
            {
                Powerup powerup = curve.Powerups[minPowerupIndex];
                if (powerup.Time > t)
                {
                    break;
                }

                minPowerupIndex++;
                Vector3 p1 = curvePos(lastCenter, lastArm, powerup.Position);
                Vector3 p0 = curvePos(center, arm, powerup.Position);
                float factor = t - powerup.Time / Res;
                powerup.SampledPosition = p1 * factor + p0 * (1 - factor);
            }

            // Move to next position
            lastCenter = center;
            lastArm = arm;
            Vector3 forward = new Vector4(sinAngle, 0.0f, cosAngle);
            center += forward * Res;

            // Add best distance
            // TODO are we sure this code runs after the runner had a chance to load best distance?
            if (Runner.BestDistance > 0.0f && t <= Runner.BestDistance && t + Res > Runner.BestDistance)
            {
                Vector3 left0 = lastCenter - lastArm;
                Vector3 left1 = center - arm;
                Vector3 right0 = lastCenter + lastArm;
                Vector3 right1 = center + arm;
                float x = (Runner.BestDistance - t) / Res;
                Vector3 bestLeft = Util.Interpolate(left0, left1, x);
                Vector3 bestRight = Util.Interpolate(right0, right1, x);
                Vector3 bestDir = bestRight - bestLeft;
                bestDir.Normalize();
                bestRight += 2.0f * bestDir;
                best.Add(bestLeft);
                best.Add(bestRight);
            }

            // Advance time
            t += Res;

            Vector4 plane = new Vector4(forward.x, forward.y, forward.z, Vector3.Dot(forward, center));
            Planes.Enqueue(plane);

            const int triggerStep = 10;
            if (i == triggerStep)
            {
                TriggerPlanes.Enqueue(plane);
            }
        }

        // Create an game object to hold the new section
        GameObject section = new GameObject();
        section.transform.parent = transform;
        section.transform.localPosition = Vector3.zero; // Mesh is built in floor-space
        section.transform.localRotation = Quaternion.identity;
        section.AddComponent<MeshFilter>();
        section.AddComponent<MeshCollider>();
        MeshRenderer renderer = section.AddComponent<MeshRenderer>();
        setMesh(section, vertices.ToArray(), normals.ToArray(), triangles.ToArray());
        renderer.material = MeshMaterial;

        sections.Enqueue(section);

        // Create best distance marker
        if (best.Count > 0)
        {
            const float lineWidth = 0.5f;

            GameObject bestObj = new GameObject();
            bestObj.name = "bestDistance";
            bestObj.transform.parent = section.transform;
            bestObj.transform.localPosition = new Vector3(0, lineWidth / 2.0f, 0);
            bestObj.transform.localRotation = Quaternion.identity;

            LineRenderer lineRenderer = bestObj.AddComponent<LineRenderer>();
            lineRenderer.SetVertexCount(best.Count);
            lineRenderer.SetPositions(best.ToArray());
            Color red = new Color(1.0f, 0.0f, 0.0f);
            lineRenderer.SetColors(red, red);
            lineRenderer.SetWidth(lineWidth, lineWidth);
            lineRenderer.material = BestMaterial;
            lineRenderer.useWorldSpace = false;
        }

        // Create game object for each block
        for (int i = 0; i < curve.Blocks.Count; i++)
        {
            Block block = curve.Blocks[i];
            GameObject blockObj = new GameObject();
            blockObj.transform.parent = section.transform;
            blockObj.AddComponent<MeshFilter>();
            blockObj.AddComponent<MeshCollider>();
            blockObj.transform.localPosition = Vector3.zero; // Mesh is built in floor-space
            blockObj.transform.localRotation = Quaternion.identity;

            Obstacle obstacle = blockObj.AddComponent<Obstacle>();
            obstacle.Block = block.Sampled;
            obstacle.Block.Height = block.Height;
            obstacle.MeshMaterial = MeshMaterial;
            obstacle.name = "block" + i;

            renderer = blockObj.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            setMesh(blockObj, block.Vertices.ToArray(), block.Normals.ToArray(), block.Triangles.ToArray());
            renderer.material = MeshMaterial;
        }

        // Create game object for each powerup
        for (int i = 0; i < curve.Powerups.Count; i++)
        {
            Powerup powerup = curve.Powerups[i];
            GameObject powerupObj = (GameObject)Instantiate(PowerupPrefab);
            powerupObj.transform.parent = section.transform;
            powerupObj.transform.localPosition = powerup.SampledPosition;
            powerupObj.transform.localRotation = Quaternion.identity;
        }
    }

    public void Clear()
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

        center = Vector3.zero;
        lastCenter = Vector3.zero;
        lastArm = Vector3.zero;
        lastLeft = -baseHalf;
        lastRight = baseHalf;
        leftV = 0.0f;
        rightV = 0.0f;
        t = 0.0f;
        angle = 0.0f;
        powerupPosition = powerupSpace;
        specialTimer = 4;

        Planes.Clear();
        TriggerPlanes.Clear();

        sections = new Queue<GameObject>();
        nextChallenge = BaseChallenge;
    }

    enum ChallengeType
    {
        Prefab,
        Random,
        Curve,
        Count
    };

    enum PrefabType
    {
        Narrow,
        Jig1,
        Random,
        Divide,
        Wall,
        Strait,
        Forest,
        Hall,
        Count,
        Empty // For testing only
    };

    enum CurveType
    {
        Constant,
        Wave,
        ZigZag,
        DoubleWave,
        Count
    }

    FloorGenerator()
    {
        Planes = new Queue<Vector4>();
        TriggerPlanes = new Queue<Vector4>();
        sections = new Queue<GameObject>();
        resetSeed(1234567890);
    }

    void resetSeed(int seed)
    {
        rng = new Rng(seed);
    }

    public void Reset(int seed)
    {
        resetSeed(seed);
        Clear();
    }

    float challengeValue(float min, float max, int challenge)
    {
        return (max + (min - max) / Mathf.Sqrt((float)challenge + 1.0f));
    }

    void fill(Curve curve, float start, float end, float left, float right, int challenge)
    {
        // Choose maximum block dimensions.
        int maxWidth, maxDepth;
        int dimType = rng.Range(0, 10);
        if (dimType < 6 || challenge < 2)
        {
            // 60% of the time, use the normal limits
            maxWidth = 3;
            maxDepth = 6;
        }
        else if (dimType == 7 || challenge < 4)
        {
            // 10% of the time, smaller limits
            maxWidth = 2;
            maxDepth = 4;
        }
        else if (dimType == 8)
        {
            // 10% of the time, small blocks
            maxWidth = 1;
            maxDepth = 2;
        }
        else
        {
            // 10% of the time, wide blocks
            maxWidth = 3;
            maxDepth = 2;
        }

        int maxHeight = 1;
        if (challenge > 8)
        {
            maxHeight = 3;
        }
        else if (challenge > 4)
        {
            maxHeight = 2;
        }

        // Track the number of blocks in the array before additions are made for the fill region.
        // Each block added is checked for overlaps with others in the fill region, other blocks are not checked,
        // it's assumed that the fill region doesn't overlap any existing blocks.
        int numBlocks = curve.Blocks.Count;

        // Choose the amount of area the blocks should cover as a fraction of the fill region's total area, scaled by challenge
        float area = (end - start) * (right - left);
        float targetArea = challengeValue(0.025f, 0.06f, challenge) * area; // Area to fill with obstacles

        // Choose a base block size that will tile the fill region evenly.
        float fillWidth = right - left;
        float fillDepth = end - start;

        int sizeX = Mathf.RoundToInt(fillWidth / 0.05f);
        int sizeZ = Mathf.FloorToInt(fillDepth / 1.0f);
        float resX = fillWidth / sizeX;
        float resZ = fillDepth / sizeZ;

        const int maxIterations = 1000;
        int itr = 0;
        while (targetArea > 0.0f)
        {
            if (itr > maxIterations)
            {
                // Something went wrong or we got incredibly unlucky, don't loop forever
                UnityEngine.Debug.LogWarning("FloorGenerator.fill() exceeded maxIterations");
                break;
            }
            itr++;

            // Choose random block dimensions and placement
            int blockWidthInt = rng.Range(1, maxWidth + 1);
            int blockDepthInt = rng.Range(2, maxDepth + 1);
            int blockLeftInt = rng.Range(0, sizeX - blockWidthInt + 1);
            int blockStartInt = rng.Range(0, sizeZ - blockDepthInt + 1);

            float blockLeft = left + blockLeftInt * resX;
            float blockRight = blockLeft + blockWidthInt * resX;
            float blockStart = start + blockStartInt * resZ;
            float blockEnd = blockStart + blockDepthInt * resZ;

            // See if it overlaps another block in this fill area
            bool overlap = false;
            for (int i = numBlocks; i < curve.Blocks.Count; i++)
            {
                Block block = curve.Blocks[i];
                if (block.Left < blockRight &&
                    block.Right > blockLeft &&
                    block.Start < blockEnd &&
                    block.Start + block.Duration > blockStart)
                {
                    overlap = true;
                    break;
                }
            }

            if (overlap)
            {
                // Just try again
                continue;
            }

            // Create the block
            {
                Block block = new Block();
                block.Left = blockLeft;
                block.Right = blockRight;
                block.Start = blockStart;
                block.Duration = blockEnd - blockStart;
                block.Height = 1.0f + rng.Range(0, maxHeight) * 0.5f;
                curve.Blocks.Add(block);
            }

            const float blockPenalty = 0.2f; // Add a fixed "penalty" for blocks to make regions with smaller blocks less dense
            targetArea -= (blockRight - blockLeft) * (blockEnd - blockStart) + blockPenalty;
        }
    }

    Curve buildNormal(int challenge)
    {
        int[] challenges = new int[(int)ChallengeType.Count];
        for (int i = 0; i < challenge; i++)
        {
            challenges[rng.Range(0, (int)ChallengeType.Count)]++;
        }

        float targetLength;
        if (challenge < 4)
        {
            targetLength = 250.0f;  // Faster early progression
        }
        else
        {
            targetLength = 500.0f;
        }

        // Build the curve
        int curveChallenge = challenges[(int)ChallengeType.Curve];
        int curveIntensity;
        CurveType curveType;
        if (curveChallenge == 0)
        {
            curveType = CurveType.Constant;
            curveIntensity = 0;
        }
        else
        {
            curveType = (CurveType)rng.Range(0, Math.Min(curveChallenge - 1, (int)CurveType.Count));
            curveIntensity = curveChallenge - (int)curveType;
        }

        Curve curve;
        float direction = rng.PlusOrMinusOne();
        float curveWidth = baseWidth;
        switch (curveType)
        {
            case CurveType.Constant:
            {
                float curveRate = challengeValue(0.0f, 0.0025f, curveIntensity);
                curve = new ConstantCurve(curveRate * direction, curveWidth);
                break;
            }

            case CurveType.Wave:
            case CurveType.DoubleWave:
            {
                float l = targetLength;
                if (curveType == CurveType.DoubleWave)
                {
                    l *= 0.5f;
                }

                float curveRate = challengeValue(0.002f, 0.005f, curveIntensity);
                curve = new SinCurve(t, l, curveRate * direction, curveWidth);
                break;
            }

            case CurveType.ZigZag:
            {
                curve = new ZigZagCurve(t, targetLength, Math.Min(10, curveIntensity), Res, curveWidth, rng);
                break;
            }

            default:
            {
                // Something went wrong
                Debug.Assert(false);
                curve = new ConstantCurve(0.0f, curveWidth);
                break;
            }
        }

        WidthModifier widthMod = new WidthModifier(curve);

        if (BuildObstacles)
        {
            // Add prefabricated obstacles
            int randomChallenge = challenges[(int)ChallengeType.Random];
            int prefabChallenge = challenges[(int)ChallengeType.Prefab];
            const float margin = 5.0f;
            float prefabStart = t + margin;
            float prefabPos = prefabStart;
            float prefabEnd = t + targetLength;

            if (challenge == 0)
            {
                // First one, add a little extra runway at the start
                prefabPos += 50.0f;
            }

            int iTest = 0;
            while (prefabPos < prefabEnd)
            {
                int maxType = Math.Min((int)PrefabType.Divide + prefabChallenge, (int)PrefabType.Count);
                PrefabType type = (PrefabType)rng.Range(0, maxType);
                //type = PrefabType.Narrow; // TEST
                /*
                if (iTest == 0)
                {
                    type = PrefabType.Hall;
                }
                else
                {
                    type = PrefabType.Empty;
                }
                iTest = iTest ^ 1;
                */
                float heightScale = challengeValue(1.0f, 4.0f, prefabChallenge);
                switch (type)
                {
                    case PrefabType.Empty:
                    {
                        // Debug only
                        prefabPos += 15.0f;
                        break;
                    }
                    case PrefabType.Narrow:
                    {
                        float reduction = challengeValue(0.4f, 0.8f, prefabChallenge);
                        float factorLeft = 1.0f;
                        float factorRight = 1.0f;
                        int side = rng.Range(0, 3);
                        if (side == 0)
                        {
                            factorLeft -= reduction;
                        }
                        else
                        {
                            factorRight -= reduction;
                        }

                        float duration = 40.0f;
                        widthMod.Changes.Add(new Vector3(prefabPos + 1, factorLeft, factorRight));
                        widthMod.Changes.Add(new Vector3(prefabPos + duration - 1, factorLeft, factorRight));
                        fill(curve, prefabPos + 2.0f, prefabPos + duration - 2.0f, 0.0f, 1.0f, randomChallenge);

                        prefabPos += duration;
                        break;
                    }

                    case PrefabType.Jig1:
                    {
                        float reduction = challengeValue(0.3f, 0.6f, prefabChallenge);
                        float duration = 50.0f;
                        float halfDuration = duration / 2.0f;
                        int startSide = rng.Range(0, 2);
                        for (int i = 0; i < 2; i++)
                        {
                            int jigSide = startSide ^ i;
                            float factorLeft = 1.0f;
                            float factorRight = 1.0f;
                            if (jigSide == 1)
                            {
                                factorLeft -= reduction;
                            }
                            else
                            {
                                factorRight -= reduction;
                            }
                            float sideStart = prefabPos + i * halfDuration;
                            widthMod.Changes.Add(new Vector3(sideStart, factorLeft, factorRight));
                            fill(curve, sideStart + 1.0f, sideStart + halfDuration - 1.0f, 0.0f, 1.0f, randomChallenge);
                        }
                        widthMod.Changes.Add(new Vector3(prefabPos + duration, 1.0f, 1.0f));

                        prefabPos += duration;
                        break;
                    }

                    case PrefabType.Divide:
                    {
                        int maxCount;
                        if (prefabChallenge < 5)
                        {
                            maxCount = 1;
                        }
                        else if (prefabChallenge < 8)
                        {
                            maxCount = 2;
                        }
                        else
                        {
                            maxCount = 3;
                        }

                        int n = rng.Range(1, maxCount + 1); // number of dividers
                        float width = 0.3f / n; // width of dividers

                        const float depth = 4.0f; // depth of dividers
                        const float duration = 28.0f; // duration of prefab including space on either side of dividers
                        float half = Mathf.Floor((duration - depth) / 2.0f); // space before and after dividers
                        float quarter = half / 2.0f;
                        float start = prefabPos + half; // begin time for dividers
                        float space = (1.0f - n * width) / (n + 1.0f); // space in between dividers

                        // make the dividers
                        for (int i = 0; i < n; i++)
                        {
                            Block block = new Block();
                            block.Start = start;
                            block.Duration = depth;
                            block.Height = 5.0f * heightScale;
                            block.Left = space * (i + 1.0f) + width * (float)i;
                            block.Right = block.Left + width;
                            curve.Blocks.Add(block);
                        }

                        // lighter fill immediately in front of the dividers
                        fill(curve, prefabPos, prefabPos + quarter, 0.0f, 1.0f, randomChallenge);
                        fill(curve, prefabPos + quarter, prefabPos + half, 0.0f, 1.0f, randomChallenge / 2);
                        fill(curve, prefabPos + duration - half, prefabPos + duration - quarter, 0.0f, 1.0f, randomChallenge / 2);
                        fill(curve, prefabPos + duration - quarter, prefabPos + duration, 0.0f, 1.0f, randomChallenge);

                        prefabPos += duration;
                        break;
                    }

                    case PrefabType.Random:
                    {
                        float duration = 25.0f;
                        fill(curve, prefabPos, prefabPos + duration, 0.0f, 1.0f, randomChallenge + 1);
                        prefabPos += duration;
                        break;
                    }

                    case PrefabType.Wall:
                    {
                        float duration = 25.0f;
                        float depth = 15.0f;
                        float width = challengeValue(0.1f, 0.3f, prefabChallenge);

                        Block block = new Block();
                        block.Start = prefabPos + (duration - depth) / 2.0f;
                        block.Duration = depth;
                        block.Height = 2.0f * heightScale;
                        block.Left = (1.0f - width) / 2.0f;
                        block.Right = (1.0f + width) / 2.0f;
                        curve.Blocks.Add(block);

                        fill(curve, prefabPos, prefabPos + duration, 0.0f, block.Left, randomChallenge);
                        fill(curve, prefabPos, prefabPos + duration, block.Right, 1.0f, randomChallenge);

                        prefabPos += duration;
                        break;
                    }

                    case PrefabType.Strait:
                    {
                        // Add some clearance before
                        prefabPos += 10.0f;

                        // Add a narrow passage
                        float width = 0.1f;
                        widthMod.Changes.Add(new Vector3(prefabPos, width, width));

                        float duration = Mathf.Round(challengeValue(10.0f, 25.0f, prefabChallenge));
                        prefabPos += duration;
                        widthMod.Changes.Add(new Vector3(prefabPos, 1.0f, 1.0f));

                        // Add some clearance after
                        prefabPos += 10.0f;

                        break;
                    }

                    case PrefabType.Forest:
                    {
                        float duration = 40.0f;
                        float blockWidth = 0.05f;

                        bool extended = (prefabChallenge >= 8 || rng.Flip());
                        if (extended)
                        {
                            duration *= 2;
                        }

                        float end = prefabPos + duration;

                        int period;
                        if (prefabChallenge < 6)
                        {
                            period = 5;
                        }
                        else if (prefabChallenge < 8 || rng.Flip())
                        {
                            period = 3;
                        }
                        else
                        {
                            period = 4;
                        }

                        float periodWidth = period * blockWidth;
                        int angle = rng.Range(1, period);
                        float space = Mathf.Round(challengeValue(8.0f, 5.0f, prefabChallenge));
                        float blockHeight = challengeValue(1.0f, 10.0f, Math.Max(0, prefabChallenge - 8));

                        float offset = rng.Range(0, period) * blockWidth;
                        while (prefabPos < end)
                        {
                            float x = offset;
                            while (x <= 1.0f - blockWidth)
                            {
                                Block block = new Block();
                                block.Start = prefabPos;
                                block.Duration = 1.0f;
                                block.Height = blockHeight;
                                block.Left = x;
                                block.Right = x + blockWidth;
                                curve.Blocks.Add(block);
                                x += periodWidth;
                            }

                            if (extended && prefabPos > end - duration / 2.0f)
                            {
                                angle *= -1;
                                extended = false; // only switch direction once
                            }

                            offset += angle * blockWidth;
                            if (offset >= periodWidth)
                            {
                                offset -= periodWidth;
                            }
                            if (offset < 0)
                            {
                                offset += periodWidth;
                            }

                            prefabPos += space;
                        }

                        break;
                    }

                    case PrefabType.Hall:
                    {
                        int count;
                        if (challenge < 7)
                        {
                            count = 2;
                        }
                        else if (challenge < 9 || rng.Flip())
                        {
                            count = 3;
                        }
                        else
                        {
                            count = 4;
                        }

                        // Add some clearance space at the beginning
                        const float clearanceSpace = 7.0f;
                        prefabPos += clearanceSpace;

                        float blockHeight = challengeValue(2.0f, 3.0f, Math.Max(0, prefabChallenge - 8));

                        float edgeWallSize = 0.025f;
                        float minExtraSize = 0.2f; // Need to be able to shift the walls around somewhat
                        float gapSize = 0.075f;
                        float availableSize = 1.0f - 2 * edgeWallSize - gapSize - minExtraSize;
                        float minWallSize = 0.17f;
                        float maxWallSize = 0.22f;
                        int minCount = Mathf.FloorToInt(availableSize / (gapSize + maxWallSize));
                        int maxCount = Mathf.FloorToInt(availableSize / (gapSize + minWallSize));
                        int wallCount = rng.Range(minCount, maxCount + 1);
                        float wallSize = availableSize / wallCount - gapSize; // leave an extra wall+gap worth of space to shift around
                        float extraSize = availableSize + minExtraSize - wallCount * (wallSize + gapSize);
                        float minOffset = Math.Max(edgeWallSize, extraSize - wallSize);
                        float maxOffset = Math.Min(extraSize - edgeWallSize, wallSize);

                        for (int i = 0; i < count; i++)
                        {
                            float offset = rng.Range(minOffset, maxOffset);
                            float x = offset - wallSize;
                            while (x < 1.0f)
                            {
                                Block block = new Block();
                                block.Start = prefabPos;
                                block.Duration = 1.0f;
                                block.Height = 2.0f;
                                block.Left = Math.Max(0.0f, x);
                                block.Right = Math.Min(1.0f, x + wallSize);
                                curve.Blocks.Add(block);
                                x = block.Right + gapSize;
                            }

                            prefabPos += 22.0f;
                        }

                        // Add some clearance space at the beginning
                        prefabPos += clearanceSpace;

                        break;
                    }

                    default: break;
                }

                // Add space between prefabs
                float prefabSpace = Mathf.Round(challengeValue(25.0f, 5.0f, prefabChallenge));
                fill(curve, prefabPos, prefabPos + prefabSpace, 0.0f, 1.0f, randomChallenge);
                prefabPos += prefabSpace;
            }
            curve.Length = prefabPos - prefabStart + margin;
        }
        else
        {
            curve.Length = targetLength;
        }

        // Scatter in powerups
        powerupPosition = Math.Max(t + 30.0f, powerupPosition);
        int blockIndex = 0;
        float powerupSize = 0.1f;
        while (powerupPosition < t + curve.Length - 20)
        {
            Block block;
            while (blockIndex < curve.Blocks.Count)
            {
                block = curve.Blocks[blockIndex];
                if (block.Start > powerupPosition)
                {
                    break;
                }
                blockIndex++;
            }

            if (blockIndex == curve.Blocks.Count)
            {
                break;
            }

            const int searchRange = 20;
            int type = rng.Range(0, 4);
            if (type < 2)
            {
                // 1/4 of powerups try to place randomly, somewhere that has a bit of clearance in front and behind
                // 1/4 place next to an edge
                const int maxTries = 100;
                for (int i = 0; i < maxTries; i++)
                {
                    float tPowerup = rng.Range(powerupPosition - 25.0f, powerupPosition + 25.0f);
                    float x;
                    if (type == 0)
                    {
                        x = rng.Flip() ? powerupSize : -powerupSize;
                    }
                    else
                    {
                        x = rng.Range(0.0f, 1.0f);
                    }
                    if (checkClearance(curve, blockIndex, tPowerup - 5.0f, tPowerup + 5.0f, x - powerupSize / 2.0f, x + powerupSize / 2.0f, searchRange))
                    {
                        curve.Powerups.Add(new Powerup(tPowerup, x));
                        powerupPosition = tPowerup;
                        break;
                    }
                }
            }
            else
            {
                // Other 1/2 of powerups try to place next to a block
                const int blockRange = 10;
                for (int iSearch = 0; iSearch < 2 * blockRange; iSearch++)
                {
                    // Search from block index outwards
                    int testIndex = blockIndex + iSearch / 2 * ((iSearch & 1) * 2 - 1);

                    // skip invalid index
                    if (testIndex < 0)
                    {
                        continue;
                    }
                    if (testIndex > curve.Blocks.Count)
                    {
                        continue;
                    }

                    // Only short blocks
                    Block testBlock = curve.Blocks[testIndex];
                    if (testBlock.Duration > 3.0f)
                    {
                        continue;
                    }

                    int startSide = rng.Range(0, 2);
                    bool success = false;
                    for (int iSide = 0; iSide < 2; iSide++)
                    {
                        int side = iSide ^ startSide;
                        float min, max;
                        const float bias = 0.0f; // shouldn't need anymore?
                        if (side == 0)
                        {
                            min = testBlock.Left - powerupSize - bias;
                            max = testBlock.Left - bias;
                        }
                        else
                        {
                            min = testBlock.Right + bias;
                            max = testBlock.Right + powerupSize + bias;
                        }

                        float tPowerup = testBlock.Start + testBlock.Duration / 2.0f;
                        if (checkClearance(curve, blockIndex, tPowerup - 5.0f, tPowerup + 5.0f, min, max, searchRange))
                        {
                            curve.Powerups.Add(new Powerup(tPowerup, (min + max) / 2.0f));
                            powerupPosition = tPowerup;
                            success = true;
                            break;
                        }
                    }

                    if (success)
                    {
                        break;
                    }
                }
            }
            powerupPosition += powerupSpace;
        }

        finalizeCurve(curve);

        widthMod.Length = curve.Length;
        return widthMod;
    }

    public void Append(int challenge)
    {
        if (specialTimer == 0)
        {
            specialTimer = 3;
            switch (rng.Range(0, 3))
            {
                case 0: AppendZigZag(challenge); break;
                case 1: AppendWave(challenge); break;
                case 2: AppendWiggle(challenge); break;
            }
            
        }
        else
        {
            // Build the section mesh
            specialTimer--;
            appendCurve(buildNormal(challenge));
        }
    }

    void addPowerups(Curve curve, int count)
    {
        float averageDistance = curve.Length / count;
        float variance = averageDistance / 4.0f;
        for (int i = 0; i < count; i++)
        {
            float c = t + (i + 0.5f) * averageDistance;
            Powerup powerup = new Powerup(rng.Range(c - variance, c + variance), rng.Range(0.0f, 1.0f));
            curve.Powerups.Add(powerup);
        }
    }

    public void AppendZigZag(int challenge)
    {
        float widthFactor;
        if (challenge < 8)
        {
            widthFactor = 1.0f / 3.7f;
        }
        else
        {
            widthFactor = 1.0f / 4.0f;
        }
        ZigZagCurve curve = new ZigZagCurve(t, Res, baseWidth * widthFactor);
        float pos = 0.0f;
        float dir = rng.PlusOrMinusOne();

        // random version
        while (true)
        {
            float straight = rng.Range(25.0f, 65.0f);
            pos += straight;
            float extraCurve;
            if (straight > 55)
            {
                extraCurve = 3.0f;
            }
            else if (straight > 45.0f)
            {
                extraCurve = 1.5f;
            }
            else
            {
                extraCurve = 0.0f;
            }
            curve.turns.Add(new ZigZagCurve.Turn(pos, rng.Range(Res * (5.0f + extraCurve), Res * (7.0f + extraCurve)), dir));
            dir *= -1.0f;

            if (pos > 250.0f)
            {
                pos += 25.0f;
                break;
            }
        }

        curve.Length = pos;
        addPowerups(curve, 3);
        finalizeCurve(curve);
        appendCurve(curve);
    }

    public void AppendWave(int challenge)
    {
        float widthFactor;
        if (challenge > 12)
        {
            widthFactor = 0.28f;
        }
        else if (challenge > 8)
        {
            widthFactor = 0.32f;
        }
        else
        {
            widthFactor = 0.36f;
        }
        Curve curve = new WaveCurve(t, 250.0f, baseWidth * widthFactor);
        addPowerups(curve, 3);
        finalizeCurve(curve);
        appendCurve(curve);
    }

    public void AppendWiggle(int challenge)
    {
        float widthFactor;
        if (challenge > 8)
        {
            widthFactor = 0.25f;
        }
        else
        {
            widthFactor = 0.28f;
        }
        Curve curve = new WiggleCurve(t, 250.0f, baseWidth * widthFactor, rng);
        addPowerups(curve, 3);
        finalizeCurve(curve);
        appendCurve(curve);
    }

    // Not used, don't like it
    public void AppendHourglass(int challenge)
    {
        float widthFactor;
        if (challenge > 8)
        {
            widthFactor = 0.15f;
        }
        else
        {
            widthFactor = 0.1f;
        }

        float period = 40.0f;
        int reps = 6;
        HourglassCurve curve = new HourglassCurve(t, period, baseWidth * widthFactor, baseWidth);
        curve.Length = reps * period;
        for (int i = 1; i < reps; i++)
        {
            Block block = new Block();
            block.Left = 0.25f;
            block.Right = 0.75f;
            block.Start = t + period * i - 12.0f;
            block.Duration = 24.0f;
            block.Height = 2.0f;
            curve.Blocks.Add(block);
        }

        finalizeCurve(curve);
        appendCurve(curve);
    }

    public void AppendRandom(int challenge)
    {
        Curve curve = new ConstantCurve(0, baseWidth);
        curve.Length = 100.0f;
        fill(curve, t, t + curve.Length, 0, 1, challenge);
        finalizeCurve(curve);
        appendCurve(curve);
    }

    bool checkClearance(Curve curve, int startIndex, float tMin, float tMax, float xMin, float xMax, int searchRange)
    {
        if (xMin < 0 || xMax > 1)
        {
            return false;
        }

        int minIndex = Math.Max(0, startIndex - searchRange);
        int maxIndex = Math.Min(curve.Blocks.Count, startIndex + searchRange);
        for (int blockIndex = minIndex; blockIndex < maxIndex; blockIndex++)
        {
            Block block = curve.Blocks[blockIndex];
            if (block.Start < tMax &&
                block.Start + block.Duration > tMin &&
                block.Left < xMax &&
                block.Right > xMin)
            {
                return false;
            }
        }
        return true;
    }

    void setMesh(GameObject gameObject, Vector3[] vertices, Vector3[] normals, int[] triangles)
    {
        Mesh mesh = new Mesh();

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.normals = normals;

        gameObject.GetComponent<MeshFilter>().mesh = mesh;
        gameObject.GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    void Start()
    {
        instance = this;

        if (!EditorMode)
        {
            restart();
        }
    }

    void restart()
    {
        Clear();
        initializing = true;
        Append(nextChallenge++);
        TriggerPlanes.Clear(); // no trigger on the first level
        Append(nextChallenge++);
        Append(nextChallenge++);
        initializing = false;
    }

    public void Advance()
    {
        Append(nextChallenge++);
        GameObject section = sections.Dequeue();
        GameObject.Destroy(section);
    }

    float postPlayTimer;
    Quaternion initialRotation;
    Quaternion targetRotation;
    Vector3 transitionPivot;
    Vector3 transitionArm;
    float transitionFactor;
    const float transitionGain = 5.0f;
    const float transitionAngle = 70.0f;
    const float transitionDistance = 25.0f;

    public Runner Runner;

    public void OnPlayerDied()
    {
        state = State.PostPlay;
        postPlayTimer = 0.0f;
    }

    void Update()
    {
        switch (state)
        {
            case State.PostPlay:
            {
                postPlayTimer += Time.deltaTime;
                const float postPlayDelay = 0.3f;
                if (postPlayTimer > postPlayDelay)
                {
                    if (Input.GetKeyDown("joystick button 0") || Input.GetKeyDown("joystick button 1") || Input.GetKeyDown("z") || Input.GetKeyDown("x"))
                    {

                        // Set up the transition out animation
                        initialRotation = transform.rotation;
                        transitionPivot = Camera.main.transform.position;
                        transitionPivot.y = 0;
                        transitionArm = Quaternion.Inverse(initialRotation) * (transitionPivot - transform.position);
                        Vector3 axis = Camera.main.transform.right;
                        Quaternion rotation = Quaternion.AngleAxis(transitionAngle, axis);
                        targetRotation = rotation * transform.rotation; // or is it the other way around

                        transitionFactor = 1.0f;
                        state = State.TransitionOut;
                    }
                }
                break;
            }
            case State.TransitionOut:
            {
                // Reverse gain
                float gain = Mathf.Min(0.99f, transitionGain * Time.deltaTime);
                float bias = 0.3f * Time.deltaTime;
                transitionFactor = (transitionFactor - gain - bias) / (1 - gain);
                if (transitionFactor <= 0.0f)
                {
                    transitionFactor = 0.0f;
                    state = State.TransitionIn;

                    // Build a new level
                    restart();

                    // Reorient so that after transitioning back in, the new level will begin right behind the camera
                    // and run in the camera's forward direction
                    Vector3 right = Vector3.Cross(Util.Up, Camera.main.transform.forward);
                    Vector3 forward = Vector3.Cross(right, Util.Up);
                    transitionPivot = Camera.main.transform.position;
                    transitionPivot.y = 0;
                    transitionArm = 10.0f * Util.Forward;
                    initialRotation = Quaternion.FromToRotation(Util.Forward, forward);
                    targetRotation = Quaternion.AngleAxis(transitionAngle, right) * initialRotation;

                    // Start the player's transition animation
                    Runner.Reset();

                }

                // Interpolate transform
                transform.rotation = Quaternion.Slerp(targetRotation, initialRotation, transitionFactor);
                transform.position = transitionPivot - transform.rotation * transitionArm + new Vector3(0, -transitionDistance * (1.0f - transitionFactor), 0);
                break;
            }
            case State.TransitionIn:
            {
                // Reverse gain
                float gain = Mathf.Min(0.99f, transitionGain * Time.deltaTime);
                float bias = 0.3f * Time.deltaTime;
                transitionFactor = transitionFactor * (1.0f - gain) + gain + bias;

                if (transitionFactor >= 1.0f)
                {
                    transitionFactor = 1.0f;
                    state = State.Play;
                }

                // Interpolate transform TODO move to a function
                transform.rotation = Quaternion.Slerp(targetRotation, initialRotation, transitionFactor);
                transform.position = transitionPivot - transform.rotation * transitionArm + new Vector3(0, -transitionDistance * (1.0f - transitionFactor), 0);

                break;
            }
        }
    }
}
