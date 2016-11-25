﻿using UnityEngine;
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

    State state = State.Play;

    const float baseWidth = 40.0f;
    const float minWidth = 10.0f;

    public float Res = 0.5f;
    public bool BuildObstacles = true;
    public Material MeshMaterial;

    Vector3 up = new Vector3(0f, 1f, 0f);
    Rng rng;

    // Curve state
    Vector3 center = Vector3.zero;
    Vector3 lastCenter = Vector3.zero;
    Vector3 lastArm = Vector3.zero;
    float t = 0.0f;
    float angle = 0.0f;
    float lastWidth = baseWidth;
    float widthV = 0.0f;

    // When true, use whatever levels were created in the editor
    public bool EditorMode = false;
    
    int nextChallenge;
    public int BaseChallenge = 0; // for debugging

    // Progress tracking
    public Queue<Vector4> Planes;

    // When crossed, remove an old section and add a new one
    // TODO a queue instead?
    public Queue<Vector4> TriggerPlanes;
    Queue<GameObject> sections;

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

    Vector3 blockLeft(Block block, Vector3 center, Vector3 arm)
    {
        return center + (block.Left * 2.0f - 1.0f) * arm;
    }

    Vector3 blockRight(Block block, Vector3 center, Vector3 arm)
    {
        return center + (block.Right * 2.0f - 1.0f) * arm;
    }

    int appendBlockVertices(Block block, Vector3 left, Vector3 right)
    {
        int vertexIndex = block.Vertices.Count;

        Vector3 vert = up * block.Height;
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
        normals.Add(up);
        normals.Add(up);

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
            normals.Add(up);
            normals.Add(up);

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

            // Move to next position
            lastCenter = center;
            lastArm = arm;
            Vector3 forward = new Vector4(sinAngle, 0.0f, cosAngle);
            center += forward * Res;
            t += Res;

            const int triggerStep = 10;
            if (i == triggerStep)
            {
                TriggerPlanes.Enqueue(new Vector4(forward.x, forward.y, forward.z, Vector3.Dot(forward, center)));
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
        t = 0.0f;
        angle = 0.0f;

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
        int sizeZ = Mathf.RoundToInt(areaDepth / 1.0f);
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

            renderer = blockObj.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            setMesh(blockObj, block.Vertices.ToArray(), block.Normals.ToArray(), block.Triangles.ToArray());
            renderer.material = MeshMaterial;
        }
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
    Vector3 initialPosition;
    Quaternion initialRotation;
    Vector3 targetPosition;
    Quaternion targetRotation;
    float transitionFactor;
    const float transitionGain = 5.0f;
    const float transitionAngle = 45.0f;
    const float transitionDistance = 25.0f;

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
                        initialPosition = transform.position;
                        initialRotation = transform.rotation;
                        Vector3 pivot = Camera.main.transform.position;
                        Vector3 arm = pivot - transform.position;
                        Vector3 axis = Camera.main.transform.right;
                        Quaternion rotation = Quaternion.AngleAxis(transitionAngle, axis);
                        Vector3 targetArm = rotation * arm;
                        Vector3 targetDisplacement = new Vector3(0, -transitionDistance, 0);
                        targetPosition = pivot + targetDisplacement - targetArm;
                        targetRotation = transform.rotation * rotation; // or is it the other way around
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
                    Vector3 up = new Vector3(0, 1, 0);
                    Vector3 right = Vector3.Cross(up, Camera.main.transform.forward);
                    Vector3 forward = Vector3.Cross(right, up);
                    Vector3 curveForward = new Vector3(0, 0, 1);
                    initialPosition = Camera.main.transform.position - 10.0f * forward;
                    initialPosition.y = 0.0f;
                    initialRotation = Quaternion.FromToRotation(curveForward, forward);
                    targetPosition = initialPosition + new Vector3(0, -transitionDistance, 0);
                    targetRotation = Quaternion.AngleAxis(transitionAngle, new Vector3(1, 0, 0)) * initialRotation;

                }

                // Interpolate transform
                transform.position = initialPosition * transitionFactor + targetPosition * (1.0f - transitionFactor);
                transform.rotation = Quaternion.Slerp(targetRotation, initialRotation, transitionFactor);
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
                transform.position = initialPosition * transitionFactor + targetPosition * (1.0f - transitionFactor);
                transform.rotation = Quaternion.Slerp(targetRotation, initialRotation, transitionFactor);

                break;
            }
        }
	}
}
