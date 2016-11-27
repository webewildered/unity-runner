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
    const float minWidth = 10.0f;

    public float Res = 0.5f;
    public bool BuildObstacles = true;
    public Material MeshMaterial;
    
    Rng rng;

    // Curve state
    Vector3 center = Vector3.zero;
    Vector3 lastCenter = Vector3.zero;
    Vector3 lastArm = Vector3.zero;
    float t = 0.0f;
    float angle = 0.0f;
    float lastWidth = baseWidth;
    float widthV = 0.0f;

    const float powerupSpace = 200.0f;
    float powerupDistance = 0.0f;

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
        public abstract void Sample(float t, out float deltaAngle, out float width);
        public List<Block> Blocks; // Must be sorted by time
        public List<Powerup> Powerups; // x is time, y is horizontal position 0 to 1

        public Curve()
        {
            Blocks = new List<Block>();
            Powerups = new List<Powerup>();
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

    class ConstantCurve : Curve
    {
        float deltaAngle;
        float width;

        public ConstantCurve(float deltaAngle, float width)
        {
            this.deltaAngle = deltaAngle;
            this.width = width;
        }

        public override void Sample(float t, out float deltaAngle, out float width)
        {
            deltaAngle = this.deltaAngle;
            width = this.width;
        }
    }

    class SinCurve : Curve
    {
        float start;
        float invPeriod;
        float magnitude;
        float width;

        public SinCurve(float start, float length, float magnitude, float width)
        {
            this.start = start;
            invPeriod = Mathf.PI * 2.0f / length;
            this.magnitude = magnitude;
            this.width = width;
        }

        public override void Sample(float t, out float deltaAngle, out float width)
        {
            t -= start;
            deltaAngle = Mathf.Sin(t * invPeriod) * magnitude;
            width = this.width;
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
        float width;
        int lastTurnIndex;

        public ZigZagCurve(float start, float length, int approxCount, float res, float width, Rng rng)
        {
            this.start = start;
            this.res = res;
            this.width = width;

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

        public override void Sample(float t, out float deltaAngle, out float width)
        {
            t -= start;
            deltaAngle = 0.0f;
            width = this.width;

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

    void appendCurve(Curve curve, float length, ref List<Vector3> vertices, ref List<Vector3> normals, ref List<int> triangles)
    {
        // Build a random mesh
        //Random.InitState((int)System.DateTime.Now.Ticks);

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
            float deltaAngle, width;
            curve.Sample(t, out deltaAngle, out width);
            angle += deltaAngle * Res;

            // Apply width gain
            // basically a spring, works well enough
            const float widthGain = 0.2f;
            float targetWidthV = (width - lastWidth) * widthGain;
            widthV = targetWidthV * widthGain + widthV * (1.0f - widthGain);
            width = lastWidth + widthV;
            lastWidth = width;

            // Add vertices at the current position
            float cosAngle = Mathf.Cos(angle);
            float sinAngle = Mathf.Sin(angle);
            Vector3 arm = new Vector3(cosAngle, 0.0f, -sinAngle) * width * 0.5f;
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
        lastWidth = baseWidth;
        t = 0.0f;
        angle = 0.0f;
        powerupDistance = powerupSpace / 2.0f;

        Planes = new Queue<Vector4>();
        TriggerPlanes = new Queue<Vector4>();
        sections = new Queue<GameObject>();
        nextChallenge = BaseChallenge;
    }

    enum ChallengeType
    {
        Prefab,
        Random,
        Size,
        Curve,
        Width,
        Count
    };

    enum PrefabType
    {
        Strait,
        Jig1,
        Random,
        Divide,
        Wall,
        Divide2,
        Divide3,
        Count
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

    void fill(Curve curve, float start, float end, float left, float right, int challenge, int sizeChallenge)
    {
        int numBlocks = curve.Blocks.Count;
        float area = (end - start) * (right - left);
        float targetArea = challengeValue(0.01f, 0.05f, challenge) * area; // Area to fill with obstacles

        float areaWidth = right - left;
        float areaDepth = end - start;

        int sizeX = Mathf.RoundToInt(areaWidth / 0.05f);
        int sizeZ = Mathf.FloorToInt(areaDepth / 1.0f);
        float resX = areaWidth / sizeX;
        float resZ = areaDepth / sizeZ;

        const int maxIterations = 1000;
        int itr = 0;
        while (targetArea > 0.0f)
        {
            if (itr > maxIterations)
            {
                // Something went wrong or we got incredibly unlucky, don't loop forever
                break;
            }
            itr++;

            // Choose random block dimensions and placement
            int blockWidthInt = Mathf.RoundToInt(rng.Range(resX, challengeValue(resX, 0.2f, sizeChallenge)) / resX);
            int blockDepthInt = Mathf.RoundToInt(rng.Range(resZ, challengeValue(resZ, 3.0f, sizeChallenge)) / resZ);
            int blockLeftInt = rng.Range(0, sizeX - blockWidthInt + 1);
            int blockStartInt = rng.Range(0, sizeZ - blockDepthInt + 1);

            float blockLeft = left + blockLeftInt * resX;
            float blockRight = blockLeft + blockWidthInt * resX;
            float blockStart = start + blockStartInt * resZ;
            float blockEnd = blockStart + blockDepthInt * resZ;

            // See if it overlaps another block in this fill area.  Assuming no other blocks
            // are placed in the fill area.
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
                block.Height = rng.Range(1.0f, challengeValue(1.5f, 3.0f, sizeChallenge));
                curve.Blocks.Add(block);
            }

            targetArea -= (blockRight - blockLeft) * (blockEnd - blockStart);
        }
    }

    void divide(Curve curve, int n, float width, float duration, float prefabStart, float heightScale, int randomChallenge, int sizeChallenge)
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
            fill(curve, prefabStart, prefabStart + (duration - depth) / 2.0f - offset, left, right, randomChallenge, sizeChallenge);
            fill(curve, prefabStart + (duration + depth) / 2.0f + offset, prefabStart + duration, left, right, randomChallenge, sizeChallenge);
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
        int widthChallenge = challenges[(int)ChallengeType.Width];
        int curveIntensity = 0;
        if (curveChallenge > 0)
        {
            curveIntensity = rng.Range(1, curveChallenge + 1);
            curveChallenge -= curveIntensity;
        }
        float curveWidth = challengeValue(baseWidth, minWidth, widthChallenge);

        Curve curve;
        float direction = rng.PlusOrMinusOne();
        CurveType curveType = (CurveType)curveChallenge;
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

        if (BuildObstacles)
        {
            // Add prefabricated obstacles
            int randomChallenge = challenges[(int)ChallengeType.Random];
            int prefabChallenge = challenges[(int)ChallengeType.Prefab];
            int sizeChallenge = challenges[(int)ChallengeType.Size];
            const float margin = 5.0f;
            float prefabStart = t + margin;
            float prefabEnd = t + length - margin;

            if (challenge == 0)
            {
                // First one, add a little extra runway at the start
                prefabStart += 50.0f;
            }

            while (prefabStart < prefabEnd)
            {
                int maxType = Math.Min((int)PrefabType.Divide + prefabChallenge, (int)PrefabType.Count);
                PrefabType type = (PrefabType)rng.Range(0, maxType);
                //type = PrefabType.Divide3; // TEST
                float heightScale = challengeValue(1.0f, 4.0f, sizeChallenge);
                switch (type)
                {
                    case PrefabType.Strait:
                    {
                        float width = challengeValue(0.15f, 0.3f, sizeChallenge);
                        float duration = 25.0f; // depth = duration
                        for (int i = 0; i < 2; i++)
                        {
                            Block block = new Block();
                            block.Start = prefabStart;
                            block.Duration = duration;
                            block.Height = 2.0f * heightScale;
                            block.Left = (i == 0) ? 0.0f : (1.0f - width);
                            block.Right = (i == 0) ? width : 1.0f;
                            curve.Blocks.Add(block);
                        }

                        fill(curve, prefabStart, prefabStart + duration, width, 1.0f - width, randomChallenge, sizeChallenge);

                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Jig1:
                    {
                        float width = challengeValue(0.4f, 0.6f, sizeChallenge);
                        float duration = 25.0f;
                        float depth = 15.0f;
                        int side = rng.Range(0, 2);

                        Block block = new Block();
                        block.Start = prefabStart + (duration - depth) / 2.0f;
                        block.Duration = depth;
                        block.Height = 2.0f * heightScale;
                        block.Left = (0 == side) ? 0.0f : (1.0f - width);
                        block.Right = (0 == side) ? width : 1.0f;
                        curve.Blocks.Add(block);

                        fill(curve, prefabStart, block.Duration, (0 == side) ? block.Right : 0.0f, (0 == side) ? 1.0f : block.Left, randomChallenge, sizeChallenge);
                        fill(curve, block.Start, block.Duration - block.Start, 0.0f, 1.0f, randomChallenge, sizeChallenge);
                        fill(curve, block.Start + block.Duration, prefabStart + duration, 0.0f, 1.0f, randomChallenge, sizeChallenge);

                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Divide:
                    {
                        float width = 0.3f;
                        float duration = 15.0f;
                        divide(curve, 1, width, duration, prefabStart, heightScale, randomChallenge, sizeChallenge);
                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Random:
                    {
                        float duration = 25.0f;
                        fill(curve, prefabStart, prefabStart + duration, 0.0f, 1.0f, randomChallenge + 1, sizeChallenge);
                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Wall:
                    {
                        float duration = 25.0f;
                        float depth = 15.0f;
                        float width = challengeValue(0.1f, 0.3f, sizeChallenge);

                        Block block = new Block();
                        block.Start = prefabStart + (duration - depth) / 2.0f;
                        block.Duration = depth;
                        block.Height = 2.0f * heightScale;
                        block.Left = (1.0f - width) / 2.0f;
                        block.Right = (1.0f + width) / 2.0f;
                        curve.Blocks.Add(block);

                        fill(curve, prefabStart, prefabStart + duration, 0.0f, block.Left, randomChallenge, sizeChallenge);
                        fill(curve, prefabStart, prefabStart + duration, block.Right, 1.0f, randomChallenge, sizeChallenge);

                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Divide2:
                    {
                        float width = 0.2f;
                        float duration = 15.0f;
                        divide(curve, 2, width, duration, prefabStart, heightScale, randomChallenge, sizeChallenge);
                        prefabStart += duration;
                        break;
                    }

                    case PrefabType.Divide3:
                    {
                        float width = 0.1f;
                        float duration = 15.0f;
                        divide(curve, 3, width, duration, prefabStart, heightScale, randomChallenge, sizeChallenge);
                        prefabStart += duration;
                        break;
                    }

                    default: break;
                }

                // Add space between prefabs
                float prefabSpace = challengeValue(25.0f, 5.0f, prefabChallenge);
                fill(curve, prefabStart, prefabStart + prefabSpace, 0.0f, 1.0f, randomChallenge, sizeChallenge);
                prefabStart += prefabSpace;
            }
        }

        // Scatter in powerups
        float powerupPosition = powerupDistance / Res;
        int blockIndex = 5; // Not at the very beginning of the section
        float powerupSize = 0.1f;
        while (powerupPosition < length - 20)
        {
            // Find the first block after the desired distance
            float minPosition = 0;
            float maxPosition = 1;
            while (blockIndex < curve.Blocks.Count)
            {
                Block block = curve.Blocks[blockIndex];
                if (block.Start > powerupPosition)
                {
                    break;
                }
                if (block.Start + block.Duration > powerupPosition - powerupSize / Res)
                {
                    if (block.Left < powerupSize)
                    {
                        minPosition = Math.Max(minPosition, block.Right);
                    }
                    if (block.Right > powerupSize)
                    {
                        maxPosition = Math.Min(maxPosition, block.Left);
                    }
                }
                blockIndex++;
            }

            // If we got to the end, or if the block is a significant way past the desired distance, try to put the powerup next to an edge instead
            if (blockIndex == curve.Blocks.Count || curve.Blocks[blockIndex].Start > powerupPosition + 25.0f / Res)
            {
                if (maxPosition - minPosition < powerupSize)
                {
                    float x = rng.Flip() ? minPosition + powerupSize / 2.0f : maxPosition - powerupSize / 2.0f;
                    curve.Powerups.Add(new Powerup(powerupPosition, x));
                }
                // Else: unexpected, just give up
            }
            else
            {
                // There may be multiple blocks at the same start position, choose among them randomly
                int testIndex = blockIndex + 1;
                while (testIndex < curve.Blocks.Count && curve.Blocks[testIndex].Start == curve.Blocks[blockIndex].Start)
                {
                    testIndex++;
                }
                blockIndex = rng.Range(blockIndex, testIndex);
                Block block = curve.Blocks[blockIndex];

                // Two ways to place, either between this block and another, or straight in front of this block.
                // Randomly pick one method to try first.  If it fails, try the other.
                int firstMode = rng.Range(0, 2);
                const int searchRange = 15;
                for (int iMethod = 0; iMethod < 2; iMethod++)
                {
                    int method = (iMethod + firstMode) & 1;
                    bool success = false;
                    if (method == 0)
                    {
                        // See if there is a neighboring block we can squeeze against
                        for (int iSearch = 0; iSearch < 2 * searchRange; iSearch++)
                        {
                            testIndex = blockIndex + iSearch / 2 * ((iSearch & 1) * 2 - 1);
                            if (testIndex < 0)
                            {
                                continue;
                            }
                            if (testIndex > curve.Blocks.Count)
                            {
                                continue;
                            }

                            // See if their start/duration overlaps
                            Block other = curve.Blocks[testIndex];
                            if (other.Start <= block.Start + block.Duration &&
                                other.Start + other.Duration >= block.Start)
                            {
                                // See if there's room between them
                                float diffLeft = block.Left - other.Right;
                                float diffRight = other.Left - block.Right;
                                float x;
                                if (diffLeft >= powerupSize && diffLeft <= 2.0f * powerupSize)
                                {
                                    x = (block.Left + other.Right) / 2.0f;
                                }
                                else if (diffRight >= powerupSize && diffRight <= 2.0f * powerupSize)
                                {
                                    x = (other.Left + block.Right) / 2.0f;
                                }
                                else
                                {
                                    // No room
                                    continue;
                                }

                                // Choose a random time in the overlapping interval
                                float start = Math.Max(block.Start, other.Start);
                                float end = Math.Max(block.Start + block.Duration, other.Start + other.Duration);
                                float t;
                                if (end - start > powerupSize)
                                {
                                    t = rng.Range(start + powerupSize / 2.0f, end - powerupSize / 2.0f);
                                }
                                else
                                {
                                    t = (end + start) / 2.0f;
                                }

                                // Check if there's some reasonable clearance in front of and behind the chosen position
                                if (checkClearance(curve, blockIndex, t - 5.0f, t + 5.0f, x - powerupSize / 2.0f, x + powerupSize / 2.0f, 15))
                                {
                                    curve.Powerups.Add(new Powerup(t, x));
                                    powerupPosition = t;
                                    success = true;
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        // Check for clearance in front of the block
                        if (!checkClearance(curve, blockIndex, block.Start - 5.0f, block.Start - 0.1f, block.Left, block.Right, searchRange))
                        {
                            continue;
                        }

                        // Check for clearance on either side
                        int startSide = rng.Range(0, 2);
                        for (int iSide = 0; iSide < 2; iSide++)
                        {
                            int side = (iSide + startSide) & 1;
                            float sideMin;
                            float sideMax;
                            if (side == 0)
                            {
                                sideMin = block.Left - 2.0f * powerupSize;
                                sideMax = block.Left - 0.1f;
                            }
                            else
                            {
                                sideMin = block.Right + 0.1f;
                                sideMax = block.Right + 2.0f * powerupSize;
                            }
                            if (checkClearance(curve, blockIndex, block.Start - 5.0f, block.Start + 5.0f, sideMin, sideMax, searchRange))
                            {
                                curve.Powerups.Add(new Powerup(block.Start - 2.5f, block.Right - powerupSize / 2.0f));
                                success = true;
                                break;
                            }
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
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();
        finalizeCurve(curve);
        appendCurve(curve, length, ref vertices, ref normals, ref triangles);

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
        for(int i = 0; i < curve.Blocks.Count; i++)
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
            GameObject powerupObj = (GameObject)Instantiate(PowerupPrefab, section.transform);
            powerupObj.transform.localPosition = powerup.SampledPosition;
        }
    }

    bool checkClearance(Curve curve, int startIndex, float tMin, float tMax, float xMin, float xMax, int searchRange)
    {
        int minIndex = Math.Max(0, startIndex - searchRange);
        int maxIndex = Math.Min(curve.Blocks.Count, startIndex + searchRange);
        for (int blockIndex = minIndex; blockIndex < maxIndex; blockIndex++)
        {
            Block block = curve.Blocks[blockIndex];
            if (block.Start <= tMax && 
                block.Start + block.Duration >= tMin &&
                block.Left <= xMax &&
                block.Right >= xMin)
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

    void Update ()
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
