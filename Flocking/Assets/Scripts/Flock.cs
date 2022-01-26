using andywiecko.BurstMathUtils;
using System;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace andywiecko.Flocking
{
    [DisallowMultipleComponent]
    public class Flock : MonoBehaviour
    {
        [field: SerializeField]
        public FlockParameters Parameters { get; private set; } = new();

        [field: SerializeField, Min(0)]
        public int BoidsCount { get; private set; } = 1000;

        public float2 TargetPosition => ((float3)targetPosition.position).xy;
        [SerializeField]
        public Transform targetPosition = default;

        private Mesh mesh;

        [Header("Init")]
        [SerializeField] private float scale = 1f;
        [SerializeField] private int boidIdPreview = 0;
        [SerializeField] private bool gizmosPreview = true;

        public Ref<NativeArray<float2>> Velocities { get; private set; }
        public Ref<NativeArray<float2>> Forces { get; private set; }
        public Ref<NativeArray<float2>> Positions { get; private set; }
        public Ref<NativeArray<Complex>> Directions { get; private set; }
        public Ref<NativeArray<FixedList4096Bytes<int>>> Neighbors { get; private set; }
        public Ref<NativeArray<FixedList4096Bytes<int>>> ReducedNeighbors { get; private set; }
        public Ref<NativeArray<FixedList4096Bytes<int>>> EnlargedNeighbors { get; private set; }

        public Ref<NativeArray<float3>> MeshVertices { get; private set; }

        private void Awake()
        {
            const Allocator Allocator = Allocator.Persistent;
            Velocities = new NativeArray<float2>(InitVelocities(), Allocator);
            Forces = new NativeArray<float2>(BoidsCount, Allocator);
            Positions = new NativeArray<float2>(InitPositions(), Allocator);
            Directions = new NativeArray<Complex>(InitDirections(), Allocator);
            Neighbors = new NativeArray<FixedList4096Bytes<int>>(BoidsCount, Allocator);
            ReducedNeighbors = new NativeArray<FixedList4096Bytes<int>>(BoidsCount, Allocator);
            EnlargedNeighbors = new NativeArray<FixedList4096Bytes<int>>(BoidsCount, Allocator);
            MeshVertices = new NativeArray<float3>(3 * BoidsCount, Allocator);

            mesh = new Mesh();
            GetComponent<MeshFilter>().mesh = mesh;

            mesh.SetVertices(MeshVertices.Value);
            mesh.SetTriangles(Enumerable.Range(0, 3 * BoidsCount).ToArray(), submesh: 0);
            mesh.SetUVs(0, Enumerable.Repeat(new Vector2(0.5f, 0.5f), 3 * BoidsCount).ToArray());

            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
        }

        private void Update()
        {
            mesh.SetVertices(MeshVertices.Value); mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            Velocities.Dispose();
            Forces.Dispose();
            Positions.Dispose();
            Directions.Dispose();
            Neighbors.Dispose();
            ReducedNeighbors.Dispose();
            EnlargedNeighbors.Dispose();
            MeshVertices.Dispose();
        }

        private float2[] InitPositions()
        {
            var positions = new float2[BoidsCount];
            var seed = 35456464u;
            var random = new Unity.Mathematics.Random(seed);

            for (int i = 0; i < BoidsCount; i++)
            {
                var r = random.NextFloat();
                var dir = random.NextFloat2Direction();
                var p = scale * r * dir;
                positions[i] = p;
            }

            return positions;
        }

        private float2[] InitVelocities()
        {
            //var random = new Unity.Mathematics.Random(seed: 26456456u);
            //return Enumerable.Range(0, BoidsCount).Select(_ => Parameters.TargetSpeed * random.NextFloat2Direction()).ToArray();
            return Enumerable.Repeat(float2.zero, BoidsCount).ToArray();
        }

        private Complex[] InitDirections()
        {
            //return Velocities.Value.Select(i => Complex.LookRotation(i)).ToArray();
            return Enumerable.Repeat(Complex.LookRotation(new(0, 1)), BoidsCount).ToArray();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            for (int i = 0; i < BoidsCount; i++)
            {
                var rh = Parameters.BoidRadius;
                var pi = Positions.Value[i];
                Gizmos.color = Color.white;
                var pxyz = math.float3(pi, 0);
                Gizmos.DrawWireSphere(pxyz, rh);

                var d = Directions.Value[i];

                var right = d.Value;
                var up = MathUtils.Rotate90CCW(right);
                Gizmos.color = Color.red;
                Gizmos.DrawRay(pxyz, rh * math.float3(right, 0));
                Gizmos.color = Color.green;
                Gizmos.DrawRay(pxyz, rh * math.float3(up, 0));

                var phi = math.PI - Parameters.BlindAngle / 2;
                var dCCW = Complex.PolarUnit(phi) * d;
                var dCW = Complex.PolarUnit(-phi) * d;
                Gizmos.color = Color.black;
                Gizmos.DrawRay(pxyz, rh * math.float3(dCCW.Value, 0));
                Gizmos.DrawRay(pxyz, rh * math.float3(dCW.Value, 0));

                if (i == boidIdPreview && gizmosPreview)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(pxyz, Parameters.InteractionRadius);
                    var n = Neighbors.Value[i];
                    var rn = ReducedNeighbors.Value[i];
                    foreach (var j in n)
                    {
                        var pj = Positions.Value[j];
                        Gizmos.color = !rn.Contains(j) ? Color.magenta : Color.cyan;
                        Gizmos.DrawLine(pxyz, math.float3(pj, 0));
                    }
                }
            }
        }
    }
}