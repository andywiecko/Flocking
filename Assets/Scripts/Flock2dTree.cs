using andywiecko.BurstCollections;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.Flocking
{
    [RequireComponent(typeof(Flock))]
    public class Flock2dTree : MonoBehaviour
    {
        public Ref<Native2dTree> Tree { get; private set; }
        public Flock Flock { get; private set; }

        public void Awake()
        {
            Flock = GetComponent<Flock>();

            var count = Flock.BoidsCount;
            Tree = new Native2dTree(count, Allocator.Persistent);
        }

        private void OnDestroy()
        {
            Tree.Dispose();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            var nodes = Tree.Value.AsReadOnly().Nodes;
            var positions = Flock.Positions.Value.AsReadOnly();
            var nodeId = Tree.Value.RootId.Value;
            var p = positions[nodeId];
            TraverseX(nodeId, p);

            void Line(float2 a, float2 b) => Gizmos.DrawLine(math.float3(a, 0), math.float3(b, 0));

            void TraverseX(int nId, float2 p)
            {
                var n = nodes[nId];
                var left = n.LeftChildId;
                if (left != -1)
                {
                    var l = positions[left];

                    Gizmos.color = Color.blue;
                    Line(new(p.x, l.y), p);

                    Gizmos.color = Color.red;
                    Line(l, new(p.x, l.y));

                    TraverseY(left, l);
                }

                var right = n.RightChildId;
                if (right != -1)
                {
                    var r = positions[right];

                    Gizmos.color = Color.blue;
                    Line(new(p.x, r.y), p);

                    Gizmos.color = Color.red;
                    Line(new(p.x, r.y), r);

                    TraverseY(right, r);
                }
            }

            void TraverseY(int nodeId, float2 p)
            {
                var n = nodes[nodeId];
                var left = n.LeftChildId;
                if (left != -1)
                {
                    var l = positions[left];

                    Gizmos.color = Color.red;
                    Line(new(l.x, p.y), p);

                    Gizmos.color = Color.blue;
                    Line(l, new(l.x, p.y));

                    TraverseX(left, l);
                }

                var right = n.RightChildId;
                if (right != -1)
                {
                    var r = positions[right];

                    Gizmos.color = Color.red;
                    Line(new(r.x, p.y), p);

                    Gizmos.color = Color.blue;
                    Line(new(r.x, p.y), r);

                    TraverseX(right, r);
                }
            }
        }
    }
}