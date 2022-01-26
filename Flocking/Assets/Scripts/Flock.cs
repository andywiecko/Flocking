using andywiecko.BurstMathUtils;
using System;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.Flocking
{
    public class Ref<T> : IDisposable where T : IDisposable
    {
        public T Value;
        public Ref(T t) => Value = t;
        public void Dispose() => Value.Dispose();
        public static implicit operator T(Ref<T> @ref) => @ref.Value;
        public static implicit operator Ref<T>(T value) => new(value);
    }

    [Serializable]
    public class FlockParameters
    {
        [field: SerializeField, Range(0, 10)]
        public float SeparationFactor { get; private set; } = 0.5f;

        [field: SerializeField, Range(0, 10)]
        public float CohesionFactor { get; private set; } = 0.5f;

        [field: SerializeField, Range(0, 10)]
        public float AlignmentFactor { get; private set; } = 0.5f;

        [field: SerializeField]
        public float InteractionRadius { get; private set; } = 2f;

        [field: SerializeField]
        public float BoidRadius { get; private set; } = 0.1f;

        [field: SerializeField]
        public float BlindAngle { get; private set; } = math.PI / 2;

        [field: SerializeField]
        public float RelaxationTime { get; private set; } = 0.5f;

        [field: SerializeField]
        public float TargetSpeed { get; private set; } = 10;

        [field: SerializeField, Min(1e-9f)]
        public float Sigma { get; private set; } = 1;

        [field: SerializeField]
        public float Mass { get; private set; } = 0.08f;

        public void Deconstruct(out float s, out float c, out float a) =>
            _ = (s = SeparationFactor, c = CohesionFactor, a = AlignmentFactor);
    }

    public class Flock : MonoBehaviour
    {
        [field: SerializeField]
        public FlockParameters Parameters { get; private set; } = new();

        [field: SerializeField, Min(0)]
        public int BoidsCount { get; private set; } = 80;

        [Header("Init")]
        [SerializeField]
        private float scale = 1f;

        [SerializeField] private int boidIdPreview = 0;
        [SerializeField] private bool gizmosPreview = true;

        public Ref<NativeArray<float2>> Velocities { get; private set; }
        public Ref<NativeArray<float2>> Forces { get; private set; }
        public Ref<NativeArray<float2>> Positions { get; private set; }
        public Ref<NativeArray<Complex>> Directions { get; private set; }
        public Ref<NativeArray<FixedList4096Bytes<int>>> Neighbors { get; private set; }
        public Ref<NativeArray<FixedList4096Bytes<int>>> ReducedNeighbors { get; private set; }
        public Ref<NativeArray<FixedList4096Bytes<int>>> EnlargedNeighbors { get; private set; }

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
        }

        private float2[] InitPositions()
        {
            var positions = new float2[BoidsCount];
            var seed = 35456464u;
            var random = new Unity.Mathematics.Random(seed);
            var boidId = 0;
            var width = (int)math.ceil(math.sqrt(BoidsCount));
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < width; j++)
                {
                retry:
                    var p = scale * (2 * random.NextFloat2() - 1);
                    if (math.length(p) > scale) goto retry;
                    positions[boidId] = p;
                    boidId++;
                    if (boidId == BoidsCount) return positions;
                }
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