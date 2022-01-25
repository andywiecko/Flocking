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
        [field: SerializeField, Range(0, 1)]
        public float SeparationFactor { get; private set; } = 0.5f;

        [field: SerializeField, Range(0, 1)]
        public float CohesionFactor { get; private set; } = 0.5f;

        [field: SerializeField, Range(0, 1)]
        public float AlignmentFactor { get; private set; } = 0.5f;

        [field: SerializeField]
        public float InteractionRadius { get; private set; } = 0.5f;

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

        public Ref<NativeArray<float>> Masses { get; private set; }
        public Ref<NativeArray<float2>> Velocities { get; private set; }
        public Ref<NativeArray<float2>> PredictedVelocities { get; private set; }
        public Ref<NativeArray<float2>> Positions { get; private set; }
        public Ref<NativeArray<Complex>> Directions { get; private set; }
        public Ref<NativeArray<FixedList4096Bytes<int>>> Neighbors { get; private set; }

        private void Awake()
        {
            const Allocator Allocator = Allocator.Persistent;
            Masses = new NativeArray<float>(InitMasses(), Allocator);
            Velocities = new NativeArray<float2>(InitVelocities(), Allocator);
            PredictedVelocities = new NativeArray<float2>(Velocities.Value, Allocator);
            Positions = new NativeArray<float2>(InitPositions(), Allocator);
            Directions = new NativeArray<Complex>(InitDirections(), Allocator);
            Neighbors = new NativeArray<FixedList4096Bytes<int>>(BoidsCount, Allocator);

            Velocities.Value[0] = 1;
        }

        private void OnDestroy()
        {
            Masses.Dispose();
            Velocities.Dispose();
            PredictedVelocities.Dispose();
            Positions.Dispose();
            Directions.Dispose();
            Neighbors.Dispose();
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
            return Enumerable.Repeat(math.float2(0, 0), BoidsCount).ToArray();
        }

        private float[] InitMasses()
        {
            return Enumerable.Repeat(1f, BoidsCount).ToArray();
        }

        private Complex[] InitDirections()
        {
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
                var p = Positions.Value[i];
                Gizmos.color = Color.white;
                var pxyz = math.float3(p, 0);
                Gizmos.DrawSphere(pxyz, 0.1f);

                var d = Directions.Value[i];

                var right = d.Value;
                var up = MathUtils.Rotate90CCW(right);
                Gizmos.color = Color.red;
                Gizmos.DrawRay(pxyz, 0.1f * math.float3(right, 0));
                Gizmos.color = Color.green;
                Gizmos.DrawRay(pxyz, 0.1f * math.float3(up, 0));

                if (i == 0)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawWireSphere(pxyz, Parameters.InteractionRadius);
                    var n = Neighbors.Value[i];
                    Gizmos.color = Color.magenta;
                    foreach (var j in n)
                    {
                        var pj = Positions.Value[j];
                        Gizmos.DrawLine(pxyz, math.float3(pj, 0));
                    }
                }
            }
        }
    }
}