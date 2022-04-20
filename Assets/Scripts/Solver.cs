using System.Linq;
using Unity.Jobs;
using UnityEngine;

namespace andywiecko.Flocking
{
    public abstract class BaseSystem : MonoBehaviour
    {
        [field: SerializeField] public int Priority { get; private set; } = 0;
        public abstract JobHandle Schedule(JobHandle dependencies);
    }

    public class Solver : MonoBehaviour
    {
        public static Flock[] Flocks;
        public static Flock2dTree[] Trees;
        private BaseSystem[] systems;

        private JobHandle dependencies;

        private void Start()
        {
            Application.targetFrameRate = 300;

            Flocks = FindObjectsOfType<Flock>();
            Trees = FindObjectsOfType<Flock2dTree>();
            systems = FindObjectsOfType<BaseSystem>();
            systems = systems.OrderBy(s => s.Priority).ToArray();
        }

        private void Update()
        {
            foreach (var s in systems)
            {
                dependencies = s.Schedule(dependencies);
            }
            dependencies.Complete();
        }
    }
}