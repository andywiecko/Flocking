using andywiecko.BurstMathUtils;
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

            public void Execute(int i) => p[i] += v[i] * dt;
            public JobHandle Schedule(JobHandle dependencies) => this.Schedule(p.Length, innerloopBatchCount: 64, dependencies);
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

            public JobHandle Schedule(JobHandle dependencies) => this.Schedule(v.Length, innerloopBatchCount: 64, dependencies);
        }

        [BurstCompile]
        private struct GenerateNeighborsJob : IJobParallelFor
        {
            private readonly float rSq;
            private NativeArray<FixedList4096Bytes<int>> neighbors;
            private NativeArray<FixedList4096Bytes<int>> reducedNeighbors;
            private NativeArray<FixedList4096Bytes<int>> enlargedNeighbors;
            private NativeArray<float2>.ReadOnly p;
            private NativeArray<Complex>.ReadOnly dir;
            private readonly float blindAngle;

            public GenerateNeighborsJob(Flock flock)
            {
                var r = flock.Parameters.InteractionRadius;
                rSq = r * r;
                neighbors = flock.Neighbors.Value;
                reducedNeighbors = flock.ReducedNeighbors.Value;
                enlargedNeighbors = flock.EnlargedNeighbors.Value;
                p = flock.Positions.Value.AsReadOnly();
                dir = flock.Directions.Value.AsReadOnly();
                blindAngle = flock.Parameters.BlindAngle;
            }

            public void Execute(int i) => (neighbors[i], reducedNeighbors[i], enlargedNeighbors[i]) = GetNeighbors(i);
            public JobHandle Schedule(JobHandle dependencies) => this.Schedule(p.Length, innerloopBatchCount: 64, dependencies);

            private (FixedList4096Bytes<int> n, FixedList4096Bytes<int> rn, FixedList4096Bytes<int> en) GetNeighbors(int i)
            {
                var n = new FixedList4096Bytes<int>();
                var rn = new FixedList4096Bytes<int>();
                var en = new FixedList4096Bytes<int>();
                for (int j = 0; j < p.Length; j++)
                {
                    if (i != j && math.distancesq(p[i], p[j]) < rSq)
                    {
                        n.Add(j);

                        var dij = Complex.NormalizeSafe(p[j] - p[i]);
                        var arg = (Complex.Conjugate(dij) * dir[i]).Arg;
                        if (math.abs(arg) < math.PI - blindAngle / 2)
                        {
                            rn.Add(j);
                        }

                    }

                    if (i != j && math.distancesq(p[i], p[j]) < 4 * rSq)
                    {
                        en.Add(j);
                    }
                }
                return (n, rn, en);
            }
        }

        [BurstCompile]
        private struct UpdateForcesJob : IJobParallelFor
        {
            private readonly float wS;
            private readonly float wC;
            private readonly float wA;
            private NativeArray<float2>.ReadOnly p;
            private NativeArray<float2>.ReadOnly v;
            private NativeArray<Complex>.ReadOnly dir;
            private NativeArray<float2> F;
            private NativeArray<FixedList4096Bytes<int>>.ReadOnly neighbors;
            private NativeArray<FixedList4096Bytes<int>>.ReadOnly reducedNeighbors;
            private NativeArray<FixedList4096Bytes<int>>.ReadOnly enlargedNeighbors;
            private readonly float rh;
            private readonly float rhSq;
            private readonly float m;
            private readonly float sigmaSq;
            private readonly float k;
            private readonly float2 p0;
            private readonly float tau;
            private readonly float v0;

            public UpdateForcesJob(Flock flock)
            {
                var parameters = flock.Parameters;
                wS = parameters.SeparationFactor;
                wC = parameters.CohesionFactor;
                wA = parameters.AlignmentFactor;
                p = flock.Positions.Value.AsReadOnly();
                v = flock.Velocities.Value.AsReadOnly();
                dir = flock.Directions.Value.AsReadOnly();
                F = flock.Forces;
                neighbors = flock.Neighbors.Value.AsReadOnly();
                reducedNeighbors = flock.ReducedNeighbors.Value.AsReadOnly();
                enlargedNeighbors = flock.EnlargedNeighbors.Value.AsReadOnly();
                rh = parameters.BoidRadius;
                rhSq = rh * rh;
                m = parameters.Mass;
                tau = parameters.RelaxationTime;
                v0 = parameters.TargetSpeed;
                var sigma = parameters.Sigma;
                sigmaSq = sigma * sigma;
                k = parameters.SpringCoefficient;
                p0 = flock.TargetPosition;
            }

            public void Execute(int i)
            {
                var fsi = GetSeparationForce(i);
                var fci = GetCohesionForce(i);
                var fai = GetAlignmentForce(i);
                var fti = GetRelaxationForce(i);
                var fki = GetSpringForce(i);
                F[i] = fsi + fci + fai + fti + fki;
            }

            private float2 GetRelaxationForce(int i)
            {
                var vi = math.length(v[i]);
                return m * (v0 - vi) * dir[i].Value / tau;
            }

            public JobHandle Schedule(JobHandle dependencies) => this.Schedule(p.Length, innerloopBatchCount: 64, dependencies);

            private float2 GetSeparationForce(int i)
            {
                var fsi = (float2)0;
                var n = neighbors[i].Length;
                foreach (var j in neighbors[i])
                {
                    var dij = p[j] - p[i];
                    var dijRh = math.length(dij) - rh;
                    var gij = dijRh <= 0 ? 1 : math.exp(-dijRh * dijRh / sigmaSq);
                    fsi += math.normalizesafe(dij);
                }

                return -wS / n * fsi;
            }

            private float2 GetCohesionForce(int i)
            {
                var n = reducedNeighbors[i].Length;

                float2 fci = 0;
                foreach (var j in reducedNeighbors[i])
                {
                    var dij = p[j] - p[i];
                    var dijLenSq = math.lengthsq(dij);
                    var xij = rhSq >= dijLenSq ? 0 : 1;
                    fci += xij * math.normalizesafe(dij);
                }

                float2 sumDij = 0;
                var nG = enlargedNeighbors[i].Length;
                foreach (var j in enlargedNeighbors[i])
                {
                    var dij = p[j] - p[i];
                    sumDij += math.normalizesafe(dij);
                }
                float Ci = nG == 0 ? 0 : math.length(sumDij) / nG;

                return n == 0 ? 0 : Ci * wC / n * fci;
            }

            private float2 GetAlignmentForce(int i)
            {
                float2 ai = 0;
                foreach (var j in reducedNeighbors[i])
                {
                    var eij = dir[j] - dir[i];
                    ai += eij.Value;
                }
                return wA * math.normalizesafe(ai);
            }

            private float2 GetSpringForce(int i)
            {
                return -k * (p[i] - p0);
            }
        }

        private struct UpdateVelocitiesJob : IJobParallelFor
        {
            private NativeArray<float2>.ReadOnly F;
            private readonly float m;
            private NativeArray<float2> v;
            private readonly float dt;

            public UpdateVelocitiesJob(Flock flock, float deltaTime)
            {
                F = flock.Forces.Value.AsReadOnly();
                m = flock.Parameters.Mass;
                v = flock.Velocities.Value;
                dt = deltaTime;
            }

            public void Execute(int i)
            {
                v[i] += F[i] / m * dt;
            }

            public JobHandle Schedule(JobHandle dependencies) => this.Schedule(v.Length, innerloopBatchCount: 64, dependencies);
        }

        [BurstCompile]
        public struct UpdateMeshJob : IJobParallelFor
        {
            private NativeArray<float2>.ReadOnly p;
            [NativeDisableParallelForRestriction]
            private NativeArray<float3> mesh;
            private NativeArray<Complex>.ReadOnly dir;
            private readonly float size;

            public UpdateMeshJob(Flock flock)
            {
                p = flock.Positions.Value.AsReadOnly();
                mesh = flock.MeshVertices;
                dir = flock.Directions.Value.AsReadOnly();
                size = 0.15f;
            }

            public void Execute(int i)
            {
                var pi = p[i];
                var diri = dir[i].Value;
                var up = MathUtils.Rotate90CCW(diri);
                mesh[3 * i + 2] = new(pi + size * diri, 0);
                mesh[3 * i + 1] = new(pi - size * diri + size * up, 0);
                mesh[3 * i + 0] = new(pi - size * diri - size * up, 0);
            }
        }

        public override JobHandle Schedule(JobHandle dependencies)
        {
            foreach (var flock in Solver.Flocks)
            {
                dependencies = new GenerateNeighborsJob(flock).Schedule(dependencies);
                dependencies = new UpdateForcesJob(flock).Schedule(dependencies);
                dependencies = new UpdateVelocitiesJob(flock, deltaTime).Schedule(dependencies);
                dependencies = new UpdatePositionsJob(flock, deltaTime).Schedule(dependencies);
                dependencies = new UpdateDirectionsJob(flock).Schedule(dependencies);
                dependencies = new UpdateMeshJob(flock).Schedule(flock.Positions.Value.Length, 64, dependencies);
            }

            return dependencies;
        }
    }
}