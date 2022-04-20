using andywiecko.BurstCollections;
using andywiecko.BurstMathUtils;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.Flocking
{
    public class Flock2dTreeSystem : BaseSystem
    {
        [SerializeField]
        private int runEveryStep = 8;

        private int step = 0;

        [BurstCompile]
        private struct ReconstructTreeJob : IJob
        {
            private Native2dTree tree;
            private NativeArray<float2>.ReadOnly positions;

            public ReconstructTreeJob(Flock2dTree flock2dtree)
            {
                tree = flock2dtree.Tree;
                positions = flock2dtree.Flock.Positions.Value.AsReadOnly();
            }

            public void Execute()
            {
                tree.Construct(positions);
            }
        }

        [BurstCompile]
        private struct GenerateNeighborsJob : IJobParallelForBatch
        {
            private readonly float r;
            private readonly float rSq;
            private NativeArray<FixedList4096Bytes<int>> neighbors;
            private NativeArray<FixedList4096Bytes<int>> reducedNeighbors;
            private NativeArray<FixedList4096Bytes<int>> enlargedNeighbors;
            private NativeArray<float2>.ReadOnly p;
            private NativeArray<Complex>.ReadOnly dir;
            private readonly float blindAngle;
            private Native2dTree.ReadOnly tree;

            public GenerateNeighborsJob(Flock2dTree flock2dtree)
            {
                r = flock2dtree.Flock.Parameters.InteractionRadius;
                rSq = r * r;
                neighbors = flock2dtree.Flock.Neighbors.Value;
                reducedNeighbors = flock2dtree.Flock.ReducedNeighbors.Value;
                enlargedNeighbors = flock2dtree.Flock.EnlargedNeighbors.Value;
                p = flock2dtree.Flock.Positions.Value.AsReadOnly();
                dir = flock2dtree.Flock.Directions.Value.AsReadOnly();
                blindAngle = flock2dtree.Flock.Parameters.BlindAngle;
                tree = flock2dtree.Tree.Value.AsReadOnly();
            }

            public void Execute(int startIndex, int count)
            {
                using var query = new NativeList<int>(Allocator.Temp);
                for (int i = 0; i < count; i++)
                {
                    query.Clear();
                    var pi = p[i];
                    var range = new AABB(pi - r, pi + r);
                    tree.RangeSearch(range, p, query);
                    Execute(startIndex + i, query);
                }
            }

            public void Execute(int i, NativeList<int> query) => (neighbors[i], reducedNeighbors[i], enlargedNeighbors[i]) = GetNeighbors(i, query);
            public JobHandle Schedule(JobHandle dependencies) => this.ScheduleBatch(p.Length, minIndicesPerJobCount: 64, dependencies);

            private (FixedList4096Bytes<int> n, FixedList4096Bytes<int> rn, FixedList4096Bytes<int> en) GetNeighbors(int i, NativeList<int> query)
            {
                var n = new FixedList4096Bytes<int>();
                var rn = new FixedList4096Bytes<int>();
                var en = new FixedList4096Bytes<int>();
                var count = 0;
                foreach (var j in query)
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
                        count++;
                        if (count == 1023)
                        {
                            return (n, rn, en);
                        }
                    }
                }
                return (n, rn, en);
            }
        }

        public override JobHandle Schedule(JobHandle dependencies)
        {
            if (step % runEveryStep == 0)
            {
                foreach (var tree in Solver.Trees)
                {
                    dependencies = new ReconstructTreeJob(tree).Schedule(dependencies);
                    dependencies = new GenerateNeighborsJob(tree).Schedule(dependencies);
                }
            }

            step++;
            return dependencies;
        }
    }
}