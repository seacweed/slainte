using System;
using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    [CreateAssetMenu(menuName = "Slainte/Fluid Experiment/Shaker Parts")]
    public sealed class FluidExperimentShakerProfile : ScriptableObject
    {
        [Tooltip("Body-local physical and pointer bounds of each removable part.")]
        public Rect strainerRect = new Rect(-.61f, -.14f, 1.22f, .96f);
        public Rect capRect = new Rect(-.3f, .44f, .6f, .47f);
        [Tooltip("Body-local paths from bowl rim to outlet; first point joins the bowl and last point defines the outlet.")]
        public Vector2[] leftGuide = Array.Empty<Vector2>();
        public Vector2[] rightGuide = Array.Empty<Vector2>();
        [Min(.001f)] public float halfThickness = .018f;
        [Min(.001f)] public float strainerMass = .08f;
        [Min(.001f)] public float capMass = .03f;
        [Min(.001f)] public float attachmentDistance = .28f;
    }
}
