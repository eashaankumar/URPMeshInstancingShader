using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Experimental.AI;
using UnityEngine.Rendering;
using static UnityEditor.ShaderGraph.Internal.KeywordDependentCollection;

public class EfficientInstancing : MonoBehaviour
{
    [SerializeField]
    Mesh Mesh;
    [SerializeField]
    int Count;
    [SerializeField]
    Material Material;
    [SerializeField]
    ShadowCastingMode ShadowCasting = ShadowCastingMode.Off;
    [SerializeField]
    bool ReceiveShadows = true;

    private ComputeBuffer instancesBuffer;
    private ComputeBuffer argsBuffer;
    private MaterialPropertyBlock MPB;
    private Bounds bounds;
    NativeArray<InstanceData> instances;
    Unity.Mathematics.Random random;
    private struct InstanceData
    {
        public float4x4 Matrix;
        public float4x4 MatrixInverse;
        public float3 Color;
        public float3 Emission;

        public static int Size()
        {
            return sizeof(float) * 4 * 4
                + sizeof(float) * 4 * 4
                + sizeof(float) * 3
                + sizeof(float) * 3;
        }
    }

    private void Start()
    {
        random = new Unity.Mathematics.Random(12);
    }

    void OnEnable()
    {
        MPB = new MaterialPropertyBlock();
        bounds = new Bounds(Vector3.zero, new Vector3(100000, 100000, 100000));
        instances = new NativeArray<InstanceData>(Count, Allocator.Persistent);
    }

    public void Update()
    {
        InitializeBuffers();
        Graphics.DrawMeshInstancedIndirect(Mesh, 0, Material, bounds, argsBuffer, 0, MPB, ShadowCasting, ReceiveShadows);
    }

    private void OnDisable()
    {
        if (instancesBuffer != null)
        {
            instancesBuffer.Release();
            instancesBuffer = null;
        }

        if (argsBuffer != null)
        {
            argsBuffer.Release();
            argsBuffer = null;
        }

        if (instances.IsCreated) instances.Dispose();
    }

    private void InitializeBuffers()
    {
        if (argsBuffer != null) argsBuffer.Release();
        if (instancesBuffer != null) instancesBuffer.Release();
        int amountPerLine = (int)Mathf.Sqrt(Count);
        int lines = amountPerLine;

        
        CreateCubesJob job = new CreateCubesJob
        {
            instances = instances,
            random = random,
            dim = amountPerLine,
            time = Time.time,
        };
        job.Schedule(Count, 64).Complete();
        /*InstanceData[] instances = new InstanceData[Count];

        for (int x = 0; x < lines; x++)
        {
            for (int z = 0; z < amountPerLine; z++)
            {
                InstanceData data = new InstanceData();

                Vector3 position = new Vector3(x + 0.5f * x, 0, z + 0.5f * z);
                Quaternion rotation = Quaternion.Euler(0, 90, 0);
                Vector3 scale = new Vector3(1, 1, 1);

                data.Matrix = Matrix4x4.TRS(position, rotation, scale);
                data.MatrixInverse = math.inverse(data.Matrix);
                data.Color = new Vector3(UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value); 
                data.Emission = data.Color * UnityEngine.Random.value * 5f;
                instances[z * amountPerLine + x] = data;
            }
        }*/

        argsBuffer = GetArgsBuffer((uint)instances.Length);
        instancesBuffer = new ComputeBuffer(instances.Length, InstanceData.Size());
        instancesBuffer.SetData(instances);
        Material.SetBuffer("_PerInstanceData", instancesBuffer);
    }


    private ComputeBuffer GetArgsBuffer(uint count)
    {
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        args[0] = (uint)Mesh.GetIndexCount(0);
        args[1] = (uint)count;
        args[2] = (uint)Mesh.GetIndexStart(0);
        args[3] = (uint)Mesh.GetBaseVertex(0);
        args[4] = 0;

        ComputeBuffer buffer = new ComputeBuffer(args.Length, sizeof(uint), ComputeBufferType.IndirectArguments);
        buffer.SetData(args);
        return buffer;
    }

    [BurstCompile]
    struct CreateCubesJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<InstanceData> instances;
        [NativeDisableParallelForRestriction]
        public Unity.Mathematics.Random random;
        [ReadOnly] public int dim;
        [ReadOnly] public float time;

        public void Execute(int index)
        {
            int2 coords = Coords(index);
            float3 position = new float3(coords.x + 0.5f * coords.x, 0, coords.y + 0.5f * coords.y);
            quaternion rotation = quaternion.Euler(0, math.radians(90), 0);
            float3 scale = new float3(1, 1, 1);
            float4x4 matrix = float4x4.TRS(position, rotation, scale);
            float3 color = RandomColor(coords);
            instances[index] = new InstanceData
            {
                Matrix = matrix,
                MatrixInverse = math.inverse(matrix),
                Color = color,
                Emission = random.NextBool() ? 0 : color * random.NextFloat(0f, 5f),
            };
        }

        public float3 RandomColor(float2 val)
        {
            float scale = 0.1f;
            var r = Unity.Mathematics.noise.snoise(new float3(val, 0) * scale + new float3(time));
            var g = Unity.Mathematics.noise.snoise(new float3(val, 10) * scale + new float3(time));
            var b = Unity.Mathematics.noise.snoise(new float3(val, 20) * scale + new float3(time));
            return new float3(r, g, b);
        }

        public int2 Coords(int i)
        {
            var x = i % dim;    // % is the "modulo operator", the remainder of i / width;
            var y = i / dim;
            return new int2(x,y);
        }
    }
}
