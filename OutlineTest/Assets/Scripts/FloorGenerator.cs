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

    public float Res = 0.5f;
    public bool BuildObstacles = true;
    public Material MeshMaterial;

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

    class ZigZagCurve : Curve
    {
        struct Turn
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

        List<Turn> turns;
        float res;
        float start;
        float maxAngle;
        float halfWidth;
        int lastTurnIndex;

        public ZigZagCurve(float start, float length, int approxCount, float res, float width, Rng rng)
        {
            this.start = start;
            this.res = res;
            halfWidth = width * 0.5f;

            maxAngle = 0.9f * Mathf.Asin(2 * res / width); // TODO - maybe could have problems when another curve varies the width

            lastTurnIndex = 0;

            turns = new List<Turn>();
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

            while (turns[lastTurnIndex].Time < t)
            {
                if (lastTurnIndex == turns.Count - 1)
                {
                    return;
                }
                lastTurnIndex++;
            }

            if (turns[lastTurnIndex].Time < t + res + turns[lastTurnIndex].Duration)
            {
                deltaAngle = maxAngle * turns[lastTurnIndex].Direction;
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

    void appendCurve(Curve curve, float length)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();

        // Add vertices connecting to the previous section
        vertices.Add(lastCenter - lastArm);
        vertices.Add(lastCenter + lastArm);
        normals.Add(Util.Up);
        normals.Add(Util.Up);

        int numSteps = (int)(length / Res) + 1;
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

            // Apply width gain
            // basically a spring, works well enough
            const float widthGain = 0.2f;
            //gainV(ref lastLeft, ref leftV, left, widthGain);
            //gainV(ref lastRight, ref rightV, right, widthGain);

            // TODO - not sure how I want to do this, for now: no smoothing
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
                if (block.Start + block.Duration + Res < t)
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
        specialTimer = 6;

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
        Divide2,
        Divide3,
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
            maxDepth = 3;
        }
        else if (dimType == 7 || challenge < 4)
        {
            // 10% of the time, smaller limits
            maxWidth = 2;
            maxDepth = 2;
        }
        else if (dimType == 8)
        {
            // 10% of the time, small blocks
            maxWidth = 1;
            maxDepth = 1;
        }
        else
        {
            // 10% of the time, wide blocks
            maxWidth = 3;
            maxDepth = 1;
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
            int blockDepthInt = rng.Range(1, maxDepth + 1);
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

    void divide(Curve curve, int n, float width, float duration, float prefabStart, float heightScale, int randomChallenge)
    {
        float depth = 4.0f;
        float start = prefabStart + (duration - depth) / 2.0f;
        float space = (1.0f - n * width) / (n + 1.0f);

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

        int divideBlockCount = curve.Blocks.Count;
        float offset = (duration - depth) * 0.2f; // leave some open space between the dividers
        for (int i = 0; i <= n; i++)
        {
            float left = (i == 0) ? 0.0f : curve.Blocks[divideBlockCount - (n - i) - 1].Right;
            float right = (i == n) ? 1.0f : curve.Blocks[divideBlockCount - (n - i)].Left;
            fill(curve, prefabStart, prefabStart + (duration - depth) / 2.0f - offset, left, right, randomChallenge);
            fill(curve, prefabStart + (duration + depth) / 2.0f + offset, prefabStart + duration, left, right, randomChallenge);
        }
    }

    public void Append(int challenge)
    {
        int[] challenges = new int[(int)ChallengeType.Count];
        for (int i = 0; i < challenge; i++)
        {
            challenges[rng.Range(0, (int)ChallengeType.Count)]++;
        }

        float length = 500.0f;

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
                float l = length;
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
                curve = new ZigZagCurve(t, length, Math.Min(10, curveIntensity), Res, curveWidth, rng);
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
            float prefabEnd = t + length - margin;

            if (challenge == 0)
            {
                // First one, add a little extra runway at the start
                prefabStart += 50.0f;
            }

            int iTest = 0;
            while (prefabStart < prefabEnd)
            {
                int maxType = Math.Min((int)PrefabType.Divide + prefabChallenge, (int)PrefabType.Count);
                PrefabType type = (PrefabType)rng.Range(0, maxType);
                //type = PrefabType.Narrow; // TEST
                if (iTest == 0)
                {
                    type = PrefabType.Hall;
                }
                else
                {
                    type = PrefabType.Empty;
                }
                iTest = iTest ^ 1;

                float heightScale = challengeValue(1.0f, 4.0f, prefabChallenge);
                switch (type)
                {
                    case PrefabType.Empty:
                    {
                        // Debug only
                        prefabStart += 15.0f;
                        break;
                    }
                    case PrefabType.Narrow:
                    {
                        float reduction = challengeValue(0.4f, 0.8f, prefabChallenge);
                        float factorLeft = 1.0f;
                        float factorRight = 1.0f;
                        int side = rng.Range(0, 3);
                        if (prefabChallenge < 4 || side == 0)
                        {
                            factorLeft -= reduction / 2.0f;
                            factorRight = 1.0f;
                        }
                        else if (side == 1)
                        {
                            factorLeft -= reduction;
                        }
                        else
                        {
                            factorRight -= reduction;
                        }

                        float duration = 50.0f;
                        widthMod.Changes.Add(new Vector3(prefabStart, factorLeft, factorRight));
                        widthMod.Changes.Add(new Vector3(prefabStart + duration, factorLeft, factorRight));
                        fill(curve, prefabStart + 1.0f, prefabStart + duration - 1.0f, 0.0f, 1.0f, randomChallenge);

                        prefabStart += duration;
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
                            float sideStart = prefabStart + i * halfDuration;
                            widthMod.Changes.Add(new Vector3(sideStart, factorLeft, factorRight));
                            fill(curve, sideStart + 1.0f, sideStart + halfDuration - 1.0f, 0.0f, 1.0f, randomChallenge);
                        }
                        widthMod.Changes.Add(new Vector3(prefabStart + duration, 1.0f, 1.0f));

                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Divide:
                    {
                        float width = 0.3f;
                        float duration = 15.0f;
                        divide(curve, 1, width, duration, prefabStart, heightScale, randomChallenge);
                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Random:
                    {
                        float duration = 25.0f;
                        fill(curve, prefabStart, prefabStart + duration, 0.0f, 1.0f, randomChallenge + 1);
                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Wall:
                    {
                        float duration = 25.0f;
                        float depth = 15.0f;
                        float width = challengeValue(0.1f, 0.3f, prefabChallenge);

                        Block block = new Block();
                        block.Start = prefabStart + (duration - depth) / 2.0f;
                        block.Duration = depth;
                        block.Height = 2.0f * heightScale;
                        block.Left = (1.0f - width) / 2.0f;
                        block.Right = (1.0f + width) / 2.0f;
                        curve.Blocks.Add(block);

                        fill(curve, prefabStart, prefabStart + duration, 0.0f, block.Left, randomChallenge);
                        fill(curve, prefabStart, prefabStart + duration, block.Right, 1.0f, randomChallenge);

                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Divide2:
                    {
                        float width = 0.2f;
                        float duration = 15.0f;
                        divide(curve, 2, width, duration, prefabStart, heightScale, randomChallenge);
                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Divide3:
                    {
                        float width = 0.1f;
                        float duration = 15.0f;
                        divide(curve, 3, width, duration, prefabStart, heightScale, randomChallenge);
                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Strait:
                    {
                        // Add some clearance before
                        prefabStart += 10.0f;

                        // Add a narrow passage
                        float width = 0.1f;
                        widthMod.Changes.Add(new Vector3(prefabStart, width, width));

                        float duration = Mathf.Round(challengeValue(10.0f, 25.0f, prefabChallenge));
                        prefabStart += duration;
                        widthMod.Changes.Add(new Vector3(prefabStart, width, width));

                        // Add some clearance after
                        prefabStart += 10.0f;
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

                        float end = prefabStart + duration;

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
                        while (prefabStart < end)
                        {
                            float x = offset;
                            while (x <= 1.0f - blockWidth)
                            {
                                Block block = new Block();
                                block.Start = prefabStart;
                                block.Duration = 1.0f;
                                block.Height = blockHeight;
                                block.Left = x;
                                block.Right = x + blockWidth;
                                curve.Blocks.Add(block);
                                x += periodWidth;
                            }

                            if (extended && prefabStart > end - duration / 2.0f)
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

                            prefabStart += space;
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
                        prefabStart += clearanceSpace;

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
                                block.Start = prefabStart;
                                block.Duration = 1.0f;
                                block.Height = 2.0f;
                                block.Left = Math.Max(0.0f, x);
                                block.Right = Math.Min(1.0f, x + wallSize);
                                curve.Blocks.Add(block);
                                x = block.Right + gapSize;
                            }

                            prefabStart += 22.0f;
                        }

                        // Add some clearance space at the beginning
                        prefabStart += clearanceSpace;

                        break;
                    }

                    default: break;
                }

                // Add space between prefabs
                float prefabSpace = Mathf.Round(challengeValue(25.0f, 5.0f, prefabChallenge));
                fill(curve, prefabStart, prefabStart + prefabSpace, 0.0f, 1.0f, randomChallenge);
                prefabStart += prefabSpace;
            }
        }

        // Scatter in powerups
        powerupPosition = Math.Max(t + 30.0f, powerupPosition);
        int blockIndex = 0;
        float powerupSize = 0.1f;
        while (powerupPosition < t + length - 20)
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

        // Build the section mesh
        finalizeCurve(curve);
        appendCurve(widthMod, length);
    }

    public void AppendRandom(int challenge)
    {
        Curve curve = new ConstantCurve(0, baseWidth);
        const float length = 100.0f;
        fill(curve, t, t + length, 0, 1, challenge);
        finalizeCurve(curve);
        appendCurve(curve, length);
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
        Append(nextChallenge++);
        TriggerPlanes.Clear(); // no trigger on the first level
        Append(nextChallenge++);
        Append(nextChallenge++);
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
                    if (Input.GetKeyDown("joystick button 0") || Input.GetKeyDown("joystick button 1"))
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
