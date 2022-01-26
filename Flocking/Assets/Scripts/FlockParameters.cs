using System;
using Unity.Mathematics;
using UnityEngine;

namespace andywiecko.Flocking
{
    [Serializable]
    public class FlockParameters
    {
        [field: SerializeField, Range(0, 10)]
        public float SeparationFactor { get; set; } = 1f;

        [field: SerializeField, Range(0, 10)]
        public float CohesionFactor { get; set; } = 1f;

        [field: SerializeField, Range(0, 10)]
        public float AlignmentFactor { get; set; } = 0.12f;

        [field: SerializeField]
        public float InteractionRadius { get; private set; } = 5f;

        [field: SerializeField]
        public float BoidRadius { get; private set; } = 0.2f;

        [field: SerializeField, Range(0, math.PI)]
        public float BlindAngle { get; private set; } = math.PI / 2;

        [field: SerializeField]
        public float RelaxationTime { get; private set; } = 0.005f;

        [field: SerializeField]
        public float TargetSpeed { get; private set; } = 10;

        [field: SerializeField, Min(1e-9f)]
        public float Sigma { get; private set; } = 1.37f;

        [field: SerializeField]
        public float Mass { get; private set; } = 0.08f;

        [field: SerializeField]
        public float SpringCoefficient { get; private set; } = 0.1f;
    }
}