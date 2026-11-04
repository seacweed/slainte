using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public sealed partial class FluidExperimentDispenser
    {
        private readonly Collider2D[] overlapHits = new Collider2D[64];
        private ContactFilter2D SolidFilter => new ContactFilter2D { useTriggers = false };
        private DistanceJoint2D suspensionJoint;
        private float retractSpeed = 12, reelSpeed, restCableLength;
        private Vector2 lastHeldPosition, heldVelocity;
        private LineRenderer hose;
        private Vector2[] hosePoints, hosePrevious;
        private float hoseAccumulator;
        private Vector2 lastHoseEnd;

        private void InitializeSuspension()
        {
            if (suspensionJoint != null) return;
            suspensionJoint = gameObject.AddComponent<DistanceJoint2D>();
            suspensionJoint.enabled = false;
            suspensionJoint.autoConfigureConnectedAnchor = false;
            suspensionJoint.autoConfigureDistance = false;
            suspensionJoint.connectedBody = null;
            suspensionJoint.maxDistanceOnly = true;
            suspensionJoint.enableCollision = false;
            suspensionJoint.anchor = hoseAttachmentLocal;
        }

        /// <summary>Only the cable length is driven. Physics2D gravity, inertia and
        /// off-center cable tension move and rotate the freely suspended tool.</summary>
        public void StepMotion(float dt)
        {
            if (!IsAvailable || dt <= 0) return;
            if (State == FluidExperimentDispenserState.Held)
            {
                Vector2 measured = Vector2.ClampMagnitude((body.Position - lastHeldPosition) / dt, Mathf.Max(2, maximumReelSpeed));
                heldVelocity = Vector2.Lerp(heldVelocity, measured, 1 - Mathf.Exp(-16 * dt));
                lastHeldPosition = body.Position;
                return;
            }
            if (!suspensionJoint.enabled) return;
            BlockedReason = null;
            if (State != FluidExperimentDispenserState.Retracting) return;
            float remaining = Mathf.Max(0, suspensionJoint.distance - restCableLength);
            // Brake the reel before its final length; never zero the body's
            // tangential motion when the line finishes taking up.
            float acceleration = Mathf.Max(.1f, reelAcceleration);
            float requestedSpeed = Mathf.Min(retractSpeed, Mathf.Sqrt(2 * acceleration * remaining));
            reelSpeed = Mathf.MoveTowards(reelSpeed, requestedSpeed, acceleration * dt);
            suspensionJoint.distance = Mathf.Max(restCableLength, suspensionJoint.distance - reelSpeed * dt);
            if (suspensionJoint.distance <= restCableLength + .0025f)
            {
                suspensionJoint.distance = restCableLength;
                if (Vector2.Distance(hoseAnchor, SuspensionPointWorld) <= restCableLength + .06f)
                    State = FluidExperimentDispenserState.Docked;
            }
        }

        private void InitializeHose()
        {
            var go = new GameObject("DispenserHose"); go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            hose = go.AddComponent<LineRenderer>(); hose.useWorldSpace = true;
            hose.startWidth = hose.endWidth = Mathf.Max(.01f, hoseWidth);
            hose.numCornerVertices = 6; hose.numCapVertices = 8; hose.sortingOrder = -3;
            if (hoseMaterial != null) hose.sharedMaterial = hoseMaterial;
            else if (bodyRenderer != null) hose.sharedMaterial = bodyRenderer.sharedMaterial;
            hose.startColor = hose.endColor = new Color(.18f, .2f, .21f, 1);
            ResetHose();
        }

        private void ResetHose()
        {
            if (hose == null) return;
            int count = Mathf.Clamp(hoseSegments, 4, 32) + 1;
            if (hosePoints == null || hosePoints.Length != count)
            { hosePoints = new Vector2[count]; hosePrevious = new Vector2[count]; }
            hose.positionCount = count * 4 + 1;
            Vector2 end = transform.TransformPoint(hoseAttachmentLocal), span = end - hoseAnchor;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                hosePoints[i] = hosePrevious[i] = Vector2.Lerp(hoseAnchor, end, t);
            }
            lastHoseEnd = end; hoseAccumulator = 0; DrawHose(end);
        }

        private void LateUpdate() => StepHose(Time.deltaTime);

        // Separate simulation entry permits real 30/60/120Hz schedule validation.
        internal void StepHose(float dt)
        {
            if (hose == null || !IsAvailable) return;
            if (hosePoints == null || hosePoints.Length != Mathf.Clamp(hoseSegments, 4, 32) + 1) ResetHose();
            Vector2 end = transform.TransformPoint(hoseAttachmentLocal);
            if (Vector2.Distance(lastHoseEnd, end) > 3) ResetHose();
            hoseAccumulator += Mathf.Clamp(dt, 0, .067f);
            const float step = 1f / 120;
            int steps = Mathf.Min(8, Mathf.FloorToInt((hoseAccumulator + .000001f) / step));
            for (int i = 0; i < steps; i++)
                SimulateHose(step, Vector2.Lerp(lastHoseEnd, end, (i + 1f) / steps));
            hoseAccumulator = Mathf.Max(0, hoseAccumulator - steps * step);
            lastHoseEnd = end; DrawHose(end);
        }

        private void SimulateHose(float dt, Vector2 end)
        {
            int last = hosePoints.Length - 1;
            Vector2 span = end - hoseAnchor;
            bool suspended = suspensionJoint != null && suspensionJoint.enabled;
            float availableLength = suspended
                ? Mathf.Max(span.magnitude, suspensionJoint.distance)
                : span.magnitude * (1 + Mathf.Clamp(hoseSlackRatio, 0, .3f)) + Mathf.Max(0, hoseExtraLength);
            // The body and the picture share one cable length. A loaded cable is
            // straight, including when Rigidbody interpolation draws an older pose.
            bool loaded = suspended
                && Vector2.Distance(hoseAnchor, SuspensionPointWorld) >= suspensionJoint.distance - .005f
                && suspensionJoint.GetReactionForce(Time.fixedDeltaTime).sqrMagnitude > .0001f;
            if (loaded || availableLength - span.magnitude < .002f)
            {
                for (int i = 0; i <= last; i++)
                    hosePoints[i] = hosePrevious[i] = Vector2.Lerp(hoseAnchor, end, i / (float)last);
                return;
            }
            for (int i = 1; i < last; i++)
            {
                Vector2 p = hosePoints[i];
                // A cord has no preferred curve or bending spring. Free nodes
                // keep their inertia; gravity takes up slack from either end.
                hosePoints[i] += (p - hosePrevious[i]) * Mathf.Exp(-2.5f * dt)
                    + Vector2.down * (9.81f * dt * dt);
                hosePrevious[i] = p;
            }
            float length = availableLength / last;
            for (int iteration = 0; iteration < 40; iteration++)
            {
                hosePoints[0] = hoseAnchor; hosePoints[last] = end;
                for (int k = 0; k < last; k++)
                {
                    int i = (iteration & 1) == 0 ? k : last - 1 - k;
                    Vector2 delta = hosePoints[i + 1] - hosePoints[i];
                    float distance = delta.magnitude;
                    // Only tension is transmitted: a slack cord cannot push its
                    // neighbors apart as a compressed spring would.
                    if (distance <= length || distance < .00001f) continue;
                    Vector2 correction = delta * ((distance - length) / distance);
                    if (i == 0) hosePoints[i + 1] -= correction;
                    else if (i + 1 == last) hosePoints[i] += correction;
                    else { hosePoints[i] += correction * .5f; hosePoints[i + 1] -= correction * .5f; }
                }
            }
            hosePoints[0] = hoseAnchor; hosePoints[last] = end;
        }

        private void DrawHose(Vector2 end)
        {
            int last = hosePoints.Length - 1;
            hosePoints[0] = hoseAnchor; hosePoints[last] = end;
            hose.startWidth = hose.endWidth = Mathf.Max(.01f, hoseWidth);
            for (int i = 0; i <= last; i++)
            {
                Vector2 b = hosePoints[i];
                Vector2 a = i == 0 ? b : (hosePoints[i - 1] + b) * .5f;
                Vector2 c = i == last ? b : (b + hosePoints[i + 1]) * .5f;
                for (int sample = 0; sample < 4; sample++)
                {
                    float t = sample * .25f;
                    // Quadratic corner rounding stays inside the solver's
                    // polygon; it cannot overshoot into a hook at a connector.
                    Vector2 p = Vector2.Lerp(Vector2.Lerp(a, b, t), Vector2.Lerp(b, c, t), t);
                    hose.SetPosition(i * 4 + sample, p);
                }
            }
            hose.SetPosition((last + 1) * 4, end);
        }
    }
}
