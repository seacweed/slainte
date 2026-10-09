using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentBody
    {
        [Header("E finite reservoir")]
        [Min(.01f)] public float reservoirResponseSeconds = .12f;
        [Min(.01f)] public float reservoirStopSeconds = .09f;
        [Min(0)] public float reservoirDripMl = .35f;
        [Range(0, 80)] public float reservoirMinimumTilt = 35;
        public FluidExperimentMaterialPreset ImprovedPourPreset => FluidExperimentMaterials.Resolve(ingredient,
            World != null && World.Liquid != null && World.Liquid.useImprovedPhysics
                ? World.Liquid.improvedMaterial : FluidExperimentMaterial.Auto);
        public float ReservoirCurrentFlowMlPerSecond => reservoirRate;
        public float ReservoirNeckMl => reservoirNeckMl;
        public float ReservoirPendingMl => pourCredit;
        private float reservoirRate, reservoirNeckMl, reservoirLastHead, reservoirLastLateral;
        private uint reservoirBirthSequence;
        private Vector2[] reservoirOutline, reservoirProjection;
        private FluidExperimentCollisionProfile reservoirProfile;

        private Vector2 ReservoirDisplayedLip
        {
            get
            {
                Vector2 side = new Vector2(-ExitDirectionLocal.y, ExitDirectionLocal.x);
                return transform.TransformPoint(LipLocal + side * reservoirLastLateral);
            }
        }

        private void ResetReservoirResponse()
        {
            reservoirRate = reservoirNeckMl = reservoirLastHead = reservoirLastLateral = 0;
            reservoirBirthSequence = 0;
        }

        private void EnsureReservoirOutline()
        {
            if (reservoirOutline != null && reservoirProfile == collisionProfile) return;
            reservoirProfile = collisionProfile;
            Vector2[] outline = null;
            double largest = 0;
            if (collisionProfile != null)
                foreach (FluidExperimentHull hull in collisionProfile.solids)
                {
                    double area = FluidExperimentReservoirModel.AreaBelow(hull.points, float.PositiveInfinity);
                    if (area <= largest) continue;
                    largest = area; outline = hull.points;
                }
            if (outline == null || outline.Length < 3)
            {
                Rect rect = pickCollider is BoxCollider2D box
                    ? new Rect(box.offset - box.size * .5f, box.size) : new Rect(-.5f, -1.5f, 1, 3);
                outline = new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                    new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) };
            }
            // The sprite's outer bottle silhouette is a reservoir proxy; capacity supplies its ml scale.
            // It does not create internal particles or change the shared solid collision profile.
            reservoirOutline = outline;
            reservoirProjection = new Vector2[outline.Length];
        }

        public float ReservoirPressureHead(float angle)
        {
            if (remainingMl <= 0 || capacityMl <= 0) return 0;
            EnsureReservoirOutline();
            Vector2 up = Physics2D.gravity.sqrMagnitude > .000001f ? -Physics2D.gravity.normalized : Vector2.up;
            Vector2 right = new Vector2(up.y, -up.x);
            Vector3 scale = transform.lossyScale;
            float radians = angle * Mathf.Deg2Rad, cosine = Mathf.Cos(radians), sine = Mathf.Sin(radians);
            Vector2 RotateScaled(Vector2 local)
            {
                Vector2 scaled = new Vector2(local.x * scale.x, local.y * scale.y);
                return new Vector2(scaled.x * cosine - scaled.y * sine, scaled.x * sine + scaled.y * cosine);
            }
            for (int i = 0; i < reservoirOutline.Length; i++)
            {
                Vector2 point = RotateScaled(reservoirOutline[i]);
                reservoirProjection[i] = new Vector2(Vector2.Dot(point, right), Vector2.Dot(point, up));
            }
            float surface = FluidExperimentReservoirModel.SurfaceHeight(reservoirProjection,
                Mathf.Clamp01(remainingMl / capacityMl), out _);
            float lipHeight = Vector2.Dot(RotateScaled(LipLocal), up);
            return Mathf.Max(0, surface - lipHeight);
        }

        public float ReservoirTargetFlow(float angle)
        {
            if (pourMlPerSecond <= 0 || remainingMl <= 0 || Physics2D.gravity.sqrMagnitude < .000001f) return 0;
            float gate = ReservoirTiltGate(angle);
            return gate <= 0 ? 0 : ReservoirFlowFromHead(ReservoirPressureHead(angle), gate);
        }

        private float ReservoirTiltGate(float angle)
        {
            float tilt = Vector2.Angle(PointAt(ExitDirectionLocal, Vector2.zero, angle), -Physics2D.gravity);
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(reservoirMinimumTilt, reservoirMinimumTilt + 25, tilt));
        }

        private float ReservoirFlowFromHead(float head, float tiltGate)
        {
            if (tiltGate <= 0 || pourMlPerSecond <= 0 || remainingMl <= 0 || Physics2D.gravity.sqrMagnitude < .000001f) return 0;
            float width = ReservoirApertureWidth();
            float aperture = Mathf.Clamp(width / .14f, .1f, 4);
            float speed = ReservoirExitSpeed(head);
            float referenceHead = Mathf.Max(.1f, Vector2.Distance(PointAt(LipLocal, Vector2.zero, 0), PointAt(Vector2.zero, Vector2.zero, 0)));
            float pressure = Mathf.Sqrt(Mathf.Clamp01(head / referenceHead));
            float authored = pourMlPerSecond * aperture * aperture * pressure * ImprovedPourPreset.FlowMultiplier * tiltGate;
            // Cross-section continuity bounds ml/s independently of particle count/resolution.
            float areaPerMl = Mathf.Max(.000001f, World.Liquid.AreaPerMl(0));
            return Mathf.Max(0, Mathf.Min(authored, width * speed / areaPerMl));
        }

        private float ReservoirApertureWidth()
        {
            Vector2 side = new Vector2(-ExitDirectionLocal.y, ExitDirectionLocal.x);
            return Mathf.Max(.005f, mouthWidth * PointAt(side, Vector2.zero, 0).magnitude);
        }

        private float ReservoirExitSpeed(float head)
            => Mathf.Min(World.Liquid.settings.gpuLiquidMaximumSpeed,
                Mathf.Sqrt(2 * Physics2D.gravity.magnitude * Mathf.Max(0, head)) * Mathf.Max(.05f, exitSpeed / 2.4f));

        private void EmitReservoirBottle(float dt)
        {
            if (remainingMl <= 0 || ingredient == null) { EndPourStream(); return; }
            if (dt <= 0) return;
            int pieces = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(StepAngle) / 5), 1, 256);
            float duration = dt / pieces;
            FluidExperimentMaterialPreset preset = ImprovedPourPreset;
            int emitted = 0;
            for (int piece = 0; piece < pieces; piece++)
            {
                float from = piece / (float)pieces, to = (piece + 1f) / pieces;
                float midpoint = PreviousAngle + StepAngle * (from + to) * .5f;
                float gate = pourMlPerSecond > 0 && Physics2D.gravity.sqrMagnitude >= .000001f
                    ? ReservoirTiltGate(midpoint) : 0;
                // Upright/disabled bottles cannot start flow. Avoid solving their
                // projected reservoir surface, but preserve the existing neck-tail
                // integration and fractional final droplet below.
                float head = gate > 0 ? ReservoirPressureHead(midpoint) : 0;
                float target = ReservoirFlowFromHead(head, gate);
                bool wet = target > .00001f;
                if (wet)
                {
                    reservoirLastHead = head;
                    reservoirNeckMl = Mathf.Min(remainingMl, reservoirDripMl * preset.DripMultiplier);
                }
                float tau = (wet ? reservoirResponseSeconds : reservoirStopSeconds) * preset.ResponseMultiplier;
                float initialRate = reservoirRate;
                float amount = FluidExperimentReservoirModel.Integral(initialRate, target, duration, tau);
                amount = Mathf.Min(amount, Mathf.Max(0, remainingMl - pourCredit));
                if (!wet) amount = Mathf.Min(amount, reservoirNeckMl);
                reservoirRate = FluidExperimentReservoirModel.Rate(initialRate, target, duration, tau);
                if (!wet) reservoirNeckMl = Mathf.Max(0, reservoirNeckMl - amount);
                bool tailFinished = !wet && (reservoirNeckMl <= .000001f || reservoirRate < .01f);
                float credit = pourCredit;
                while (remainingMl > 0)
                {
                    float volume = Mathf.Min(World.Liquid.ParticleVolumeMl, remainingMl);
                    if (credit + amount + .000001f < volume) break;
                    if (++emitted > 64) { EndPourStream(); return; }
                    float birth = FluidExperimentReservoirModel.BirthTime(initialRate, target, duration, tau, Mathf.Max(0, volume - credit));
                    float fraction = Mathf.Lerp(from, to, birth / duration);
                    float rate = FluidExperimentReservoirModel.Rate(initialRate, target, birth, tau);
                    if (!EmitReservoirParticle(volume, fraction, rate, wet ? head : reservoirLastHead, dt))
                    { EndPourStream(); return; }
                    credit -= volume;
                }
                pourCredit = Mathf.Max(0, credit + amount);
                if (tailFinished && pourCredit > .000001f && remainingMl > 0)
                {
                    // A fractional final droplet carries actual ml, never a visual-only fake particle.
                    float volume = Mathf.Min(pourCredit, remainingMl);
                    if (++emitted > 64 || !EmitReservoirParticle(volume, to, reservoirRate, reservoirLastHead, dt))
                    { EndPourStream(); return; }
                    pourCredit = 0;
                }
                if (tailFinished || remainingMl <= 0) EndPourStream();
            }
        }

        private bool EmitReservoirParticle(float volume, float fraction, float flow, float head, float dt)
        {
            float angle = PreviousAngle + StepAngle * fraction;
            float speed = ReservoirExitSpeed(head);
            float areaPerMl = Mathf.Max(.000001f, World.Liquid.AreaPerMl(0));
            float streamWidth = Mathf.Min(ReservoirApertureWidth(), Mathf.Max(.005f, flow * areaPerMl / Mathf.Max(.05f, speed)));
            // Deterministic low-discrepancy aperture samples avoid a single compressed particle column.
            float phase = Mathf.Repeat((reservoirBirthSequence + 1) * .61803398875f, 1) - .5f;
            Vector2 sideLocal = new Vector2(-ExitDirectionLocal.y, ExitDirectionLocal.x);
            float sideScale = Mathf.Max(.00001f, PointAt(sideLocal, Vector2.zero, 0).magnitude);
            float lateral = phase * streamWidth * .8f / sideScale;
            Vector2 nozzle = ReservoirNozzleAt(fraction, sideLocal * lateral);
            Vector2 origin = Vector2.Lerp(PreviousPosition, Position, fraction);
            Vector2 arm = nozzle - origin;
            Vector2 inherited = EmissionPointVelocity(arm, dt);
            Vector2 direction = PointAt(ExitDirectionLocal, Vector2.zero, angle).normalized;
            if (pourStream == 0) pourStream = World.Liquid.NewPourStream();
            float radius = Mathf.Clamp(Mathf.Min(streamWidth * .5f, Mathf.Sqrt(volume * areaPerMl / Mathf.PI)), .008f, World.Liquid.Radius * 1.5f);
            if (!World.Liquid.TryEmitStream(nozzle, inherited + direction * speed, ingredient, volume,
                Id, pourStream, lastPourToken, fraction * dt, radius, out uint token)) return false;
            remainingMl = Mathf.Max(0, remainingMl - volume);
            lastPourToken = token; lastPourRadius = radius;
            reservoirLastLateral = lateral; reservoirBirthSequence++;
            return true;
        }

        private Vector2 ReservoirNozzleAt(float fraction, Vector2 lateralLocal)
        {
            float angle = PreviousAngle + StepAngle * fraction;
            Vector2 position = Vector2.Lerp(PreviousPosition, Position, fraction);
            if (collisionProfile == null)
                return NozzleAt(fraction) + PointAt(lateralLocal, Vector2.zero, angle);
            Vector2 nozzle = PointAt(LipLocal + lateralLocal, Vector2.zero, 0);
            Vector2 direction = PointAt(ExitDirectionLocal, Vector2.zero, 0).normalized;
            float radius = World.Liquid.Radius * 1.05f;
            for (int i = 0; i < 512; i++)
            {
                float clearance = SolidClearance(nozzle);
                if (clearance >= radius) return position + Rotate(nozzle, angle);
                nozzle += direction * Mathf.Max(radius * .25f, radius - clearance);
            }
            throw new System.InvalidOperationException(name + ": reservoir aperture cannot clear the solid profile.");
        }
    }
}
