using andywiecko.BurstMathUtils;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.Flocking
{
    public class FlockSystem : BaseSystem
    {
        [SerializeField]
        private float deltaTime = 0.01f;

        [BurstCompile]
        private struct UpdatePositionsJob : IJobParallelFor
        {
            private NativeArray<float2> p;
            private NativeArray<float2>.ReadOnly v;
            private readonly float dt;

            public UpdatePositionsJob(Flock flock, float deltaTime)
            {
                p = flock.Positions;
                v = flock.Velocities.Value.AsReadOnly();
                dt = deltaTime;
            }

            public void Execute(int i)
            {
                p[i] += v[i] * dt;
            }

            public JobHandle Schedule(JobHandle dependencies)
            {
                return this.Schedule(p.Length, innerloopBatchCount: 64, dependencies);
            }
        }

        [BurstCompile]
        private struct UpdateDirectionsJob : IJobParallelFor
        {
            private NativeArray<Complex> dir;
            private NativeArray<float2>.ReadOnly v;

            public UpdateDirectionsJob(Flock flock)
            {
                dir = flock.Directions;
                v = flock.Velocities.Value.AsReadOnly();
            }

            public void Execute(int i)
            {
                dir[i] = Complex.LookRotationSafe(v[i], dir[i].Value);
            }

            public JobHandle Schedule(JobHandle dependencies)
            {
                return this.Schedule(v.Length, innerloopBatchCount: 64, dependencies);
            }
        }

        [BurstCompile]
        private struct GenerateNeighborsJob : IJobParallelFor
        {
            private readonly float rSq;
            private NativeArray<FixedList4096Bytes<int>> neighbors;
            private NativeArray<float2>.ReadOnly p;

            public GenerateNeighborsJob(Flock flock)
            {
                var r = flock.Parameters.InteractionRadius;
                rSq = r * r;
                neighbors = flock.Neighbors.Value;
                p = flock.Positions.Value.AsReadOnly();
            }

            public void Execute(int i)
            {
                neighbors[i] = GetNeighbors(i);
            }

            public JobHandle Schedule(JobHandle dependencies)
            {
                return this.Schedule(p.Length, innerloopBatchCount: 64, dependencies);
            }

            private FixedList4096Bytes<int> GetNeighbors(int i)
            {
                var list = new FixedList4096Bytes<int>();
                for (int j = 0; j < p.Length; j++)
                {
                    if (i != j && math.distancesq(p[i], p[j]) < rSq)
                    {
                        list.Add(j);
                    }
                }
                return list;
            }
        }

        [BurstCompile]
        private struct CalculatePredictedVelocitiesJob : IJobParallelFor
        {
            private readonly float S;
            private readonly float C;
            private readonly float A;
            private NativeArray<float2>.ReadOnly p;
            private NativeArray<float2>.ReadOnly v;
            private NativeArray<Complex>.ReadOnly dir;
            private NativeArray<float2> vNew;
            private NativeArray<FixedList4096Bytes<int>>.ReadOnly neighbors;
            private readonly float boidRadius;
            private readonly float blindAngle;

            public CalculatePredictedVelocitiesJob(Flock flock)
            {
                (S, C, A) = flock.Parameters;
                p = flock.Positions.Value.AsReadOnly();
                v = flock.Velocities.Value.AsReadOnly();
                dir = flock.Directions.Value.AsReadOnly();
                vNew = flock.PredictedVelocities;
                neighbors = flock.Neighbors.Value.AsReadOnly();
                boidRadius = flock.Parameters.BoidRadius;
                blindAngle = flock.Parameters.BlindAngle;
            }

            public void Execute(int i)
            {
                var si = GetSeparation(i);
                var ci = GetCohesion(i);
                var ai = GetAlignment(i);
                vNew[i] = v[i] + S * si + C * ci + A * ai;
            }

            public JobHandle Schedule(JobHandle dependencies)
            {
                return this.Schedule(p.Length, innerloopBatchCount: 64, dependencies);
            }

            private float2 GetSeparation(int i)
            {
                var si = (float2)0;
                var m = neighbors[i].Length;
                foreach (var j in neighbors[i])
                {
                    var dij = p[j] - p[i];
                    si += math.normalizesafe(dij);
                }

                return -si / m;
            }

            private float2 GetCohesion(int i)
            {
                var rhSq = boidRadius * boidRadius;
                var m = neighbors[i].Length;

                float2 ci = 0;
                foreach (var j in neighbors[i])
                {
                    var dij = p[j] - p[i];
                    var dijLenSq = math.lengthsq(dij);
                    var xij = rhSq > dijLenSq ? 0 : 1;
                    ci += xij * dij;
                }
                return m != 0 ? ci / m : 0;
            }

            private float2 GetAlignment(int i)
            {
                float2 ai = 0;
                foreach (var j in neighbors[i])
                {
                    var dij = Complex.NormalizeSafe(p[j] - p[i]);
                    var arg = (Complex.Conjugate(dij) * dir[i]).Arg;
                    if (math.abs(arg) > math.PI - blindAngle / 2)
                    {
                        continue;
                    }

                    var eij = dir[j] - dir[i];
                    ai += eij.Value;
                }
                return math.normalizesafe(ai);
            }
        }

        private struct UpdateVelocitiesJob : IJobParallelFor
        {
            private NativeArray<float2>.ReadOnly vNew;
            private NativeArray<float2> v;

            public UpdateVelocitiesJob(Flock flock)
            {
                vNew = flock.PredictedVelocities.Value.AsReadOnly();
                v = flock.Velocities.Value;
            }

            public void Execute(int i)
            {
                v[i] = vNew[i];
            }

            public JobHandle Schedule(JobHandle dependencies)
            {
                return this.Schedule(v.Length, innerloopBatchCount: 64, dependencies);
            }
        }

        public override JobHandle Schedule(JobHandle dependencies)
        {
            foreach (var flock in Solver.Flocks)
            {
                dependencies = new GenerateNeighborsJob(flock).Schedule(dependencies);
                dependencies = new CalculatePredictedVelocitiesJob(flock).Schedule(dependencies);
                dependencies = new UpdateVelocitiesJob(flock).Schedule(dependencies);
                dependencies = new UpdatePositionsJob(flock, deltaTime).Schedule(dependencies);
                dependencies = new UpdateDirectionsJob(flock).Schedule(dependencies);
            }

            return dependencies;
        }
    }
}