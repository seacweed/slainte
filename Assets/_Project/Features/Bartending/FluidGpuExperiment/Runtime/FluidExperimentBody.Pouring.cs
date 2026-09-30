using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        [Header("Bottle stream (FluidExperiment only)")]
        [Tooltip("Zero uses the existing collider-center-to-mouth direction.")]
        public Vector2 mouthDirectionLocal;
        [Min(.02f)] public float mouthWidth = .14f;
        public bool overrideMouthLip;
        public Vector2 mouthLipLocal;
        private uint pourStream, lastPourToken;
        private float lastPourRadius;

        public Vector2 ExitDirectionLocal
        {
            get
            {
                Vector2 center = pickCollider is BoxCollider2D box ? box.offset : Vector2.zero;
                Vector2 direction = mouthDirectionLocal.sqrMagnitude > .000001f ? mouthDirectionLocal : mouthLocal - center;
                return direction.sqrMagnitude > .000001f ? direction.normalized : Vector2.up;
            }
        }
        public Vector2 LipLocal
        {
            get
            {
                if (overrideMouthLip) return mouthLipLocal;
                // Existing builder scales bottle art to 3.4 local units and stores its outward offset in mouthLocal.
                Sprite sprite = ingredient != null ? ingredient.icon : null;
                float outward = sprite != null ? ingredient.liquidSpawnOutwardPixels / sprite.pixelsPerUnit
                    * (3.4f / sprite.bounds.size.y) : 0;
                return mouthLocal - Vector2.up * outward;
            }
        }
        public Vector2 NozzleAt(float fraction)
        {
            float angle = PreviousAngle + StepAngle * fraction;
            Vector2 position = Vector2.Lerp(PreviousPosition, Position, fraction);
            Vector2 mouth = PointAt(mouthLocal, Vector2.zero, 0);
            Vector2 direction = PointAt(ExitDirectionLocal, Vector2.zero, 0).normalized;
            float offset = World.Liquid.Radius * 1.5f;
            if (collisionProfile != null)
            {
                Vector2 nozzle = PointAt(LipLocal, Vector2.zero, 0);
                float radius = World.Liquid.Radius * 1.05f;
                for (int i = 0; i < 512; i++)
                {
                    float clearance = SolidClearance(nozzle);
                    if (clearance >= radius) return position + Rotate(nozzle, angle);
                    nozzle += direction * Mathf.Max(radius * .25f, radius - clearance);
                }
                throw new System.InvalidOperationException(name + ": mouth direction cannot clear the solid profile.");
            }
            if (pickCollider is BoxCollider2D box)
            {
                Vector2 a = PointAt(box.offset - box.size * .5f, Vector2.zero, 0);
                Vector2 b = PointAt(box.offset + box.size * .5f, Vector2.zero, 0);
                Vector2 min = Vector2.Min(a,b) - Vector2.one * offset;
                Vector2 max = Vector2.Max(a,b) + Vector2.one * offset;
                if (mouth.x >= min.x && mouth.x <= max.x && mouth.y >= min.y && mouth.y <= max.y)
                {
                    float x = Mathf.Abs(direction.x) > .000001f ? ((direction.x > 0 ? max.x : min.x) - mouth.x) / direction.x : float.PositiveInfinity;
                    float y = Mathf.Abs(direction.y) > .000001f ? ((direction.y > 0 ? max.y : min.y) - mouth.y) / direction.y : float.PositiveInfinity;
                    offset = Mathf.Max(offset, Mathf.Min(x,y) + .001f);
                }
            }
            return position + Rotate(mouth + direction * offset, angle);
        }
        public float PourFlowAt(float angle)
        {
            float tilt = Vector2.Angle(Rotate(ExitDirectionLocal, angle), Vector2.up);
            float t = Mathf.InverseLerp(pourStartAngle, Mathf.Max(pourStartAngle + .1f, fullPourAngle), tilt);
            return t * t * (3 - 2 * t);
        }
        private void EndPourStream()
        {
            pourStream = lastPourToken = 0;
            pourCredit = 0;
            ResetReservoirResponse();
        }
        private void EmitBottle(float dt)
        {
            if (World.Liquid.useImprovedPhysics) { EmitReservoirBottle(dt); return; }
            if (remainingMl <= 0 || dt <= 0 || pourMlPerSecond <= 0) { EndPourStream(); return; }
            // Refine the emitter's angular path, never the simulation schedule of other liquid.
            int pieces = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(StepAngle) / 5), 1, 256);
            float duration = dt / pieces;
            int emitted = 0;
            for (int piece = 0; piece < pieces; piece++)
            {
                float from = piece / (float)pieces, to = (piece + 1f) / pieces;
                float flowA = PourFlowAt(PreviousAngle + StepAngle * from);
                float flowB = PourFlowAt(PreviousAngle + StepAngle * to);
                float amount = pourMlPerSecond * duration * (flowA + flowB) * .5f;
                if (amount <= .000001f) { EndPourStream(); continue; }
                float credit = pourCredit;
                while (remainingMl > 0)
                {
                    float volume = Mathf.Min(World.Liquid.ParticleVolumeMl, remainingMl);
                    if (credit + amount + .000001f < volume) break;
                    if (++emitted > 64) { EndPourStream(); return; }
                    float wanted = Mathf.Max(0, volume - credit) / (pourMlPerSecond * duration);
                    float lo = 0, hi = 1;
                    for (int i = 0; i < 20; i++)
                    {
                        float t = (lo + hi) * .5f;
                        float integral = flowA * t + (flowB - flowA) * t * t * .5f;
                        if (integral < wanted) lo = t; else hi = t;
                    }
                    float localTime = (lo + hi) * .5f;
                    float fraction = Mathf.Lerp(from, to, localTime);
                    float angle = PreviousAngle + StepAngle * fraction;
                    float flow = Mathf.Lerp(flowA, flowB, localTime);
                    Vector2 nozzle = NozzleAt(fraction);
                    Vector2 origin = Vector2.Lerp(PreviousPosition, Position, fraction);
                    Vector2 arm = nozzle - origin;
                    Vector2 mouthVelocity = (Position - PreviousPosition) / dt
                        + new Vector2(-arm.y, arm.x) * (StepAngle * Mathf.Deg2Rad / dt);
                    // Match the existing Fill packing distance (1.75 radii): avoid a compressed stack at the nozzle.
                    // Only this emitter's initial speed changes; no global particle size or solver schedule changes.
                    float packingSpeed = pourMlPerSecond * flow / Mathf.Max(.0001f, World.Liquid.ParticleVolumeMl)
                        * World.Liquid.Radius * 1.75f;
                    float nozzleSpeed = Mathf.Min(World.Liquid.settings.gpuLiquidMaximumSpeed,
                        Mathf.Max(exitSpeed * Mathf.Sqrt(flow), packingSpeed));
                    Vector2 velocity = mouthVelocity + Rotate(ExitDirectionLocal, angle) * nozzleSpeed;
                    if (pourStream == 0) pourStream = World.Liquid.NewPourStream();
                    float radius = Mathf.Clamp(mouthWidth * Mathf.Abs(transform.lossyScale.x) * .5f * Mathf.Sqrt(flow), .018f, World.Liquid.Radius * 1.5f);
                    if (!World.Liquid.TryEmitStream(nozzle, velocity, ingredient, volume, Id, pourStream,
                            lastPourToken, fraction * dt, radius, out uint token))
                    { EndPourStream(); return; } // No deferred burst or stock deduction on a rejected spawn.
                    lastPourToken = token; lastPourRadius = radius;
                    remainingMl -= volume; credit -= volume;
                }
                pourCredit = Mathf.Max(0, credit + amount);
                if (flowB <= .000001f || remainingMl <= 0) EndPourStream();
            }
        }
        internal bool TryGetStreamHead(out GpuLiquidStreamHead head)
        {
            head = default;
            bool improved = World != null && World.Liquid != null && World.Liquid.useImprovedPhysics;
            bool flowing = improved ? ReservoirCurrentFlowMlPerSecond > .000001f : PourFlowAt(Angle) > .000001f;
            if (pourStream == 0 || lastPourToken == 0 || !flowing || remainingMl <= 0) return false;
            // Follow the displayed Rigidbody interpolation at the lip, without pulling detached particles.
            head = new GpuLiquidStreamHead { Lip = improved ? ReservoirDisplayedLip : (Vector2)transform.TransformPoint(LipLocal), Token = lastPourToken,
                StreamId = pourStream, SourceId = Id, Radius = lastPourRadius };
            return true;
        }
    }
}
