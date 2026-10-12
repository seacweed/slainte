using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    // Explicit opt-in on the hurricane prefab. Native impact queues a transition;
    // the world commits it after Physics2D and before GPU geometry is uploaded.
    [DisallowMultipleComponent, RequireComponent(typeof(FluidExperimentBody))]
    public sealed class FluidExperimentBreakableGlass : MonoBehaviour
    {
        public FluidExperimentBreakageProfile profile;
        public FluidExperimentBody Body { get; private set; }
        public bool IsBroken { get; private set; }
        public bool BreakQueued => breakQueued;
        public float LastImpactSpeed { get; private set; }
        public float LastImpactImpulse { get; private set; }
        public Vector2 LastImpactPoint { get; private set; }
        private FluidExperimentWorld world;
        private bool breakQueued, motionCaptured;
        private Vector2 beforeVelocity, beforeCenter;
        private float beforeSpin;

        private void Awake() => Body = GetComponent<FluidExperimentBody>();
        private void OnEnable()
        {
            if (Body == null) Body = GetComponent<FluidExperimentBody>();
            world = GetComponentInParent<FluidExperimentWorld>();
            world?.RegisterBreakable(this);
            motionCaptured = false;
        }
        private void OnDisable() { motionCaptured = false; breakQueued = false; }
        private void OnDestroy() { if (world != null) world.UnregisterBreakable(this); }
        private bool CanBreak => !IsBroken && isActiveAndEnabled && Body != null && Body.isActiveAndEnabled
            && !Body.IsHeld && Body.kind == LabItemKind.Glass && Body.Body != null
            && Body.Body.simulated && Body.Body.bodyType == RigidbodyType2D.Dynamic
            && profile != null && profile.shards.Length > 0 && world != null
            && world.Liquid != null && world.Liquid.CohesivePhysicsActive;

        internal void CaptureBeforePhysics()
        {
            motionCaptured = CanBreak;
            if (!motionCaptured) return;
            beforeVelocity = Body.Body.linearVelocity;
            beforeCenter = Body.Body.worldCenterOfMass;
            beforeSpin = Body.Body.angularVelocity * Mathf.Deg2Rad;
        }
        private void OnCollisionEnter2D(Collision2D collision) => MeasureImpact(collision);
        private void OnCollisionStay2D(Collision2D collision) => MeasureImpact(collision);
        private void MeasureImpact(Collision2D collision)
        {
            if (!CanBreak || breakQueued) return;
            float impulse = 0, speed = 0;
            Vector2 point = Body.Position;
            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                impulse += Mathf.Max(0, contact.normalImpulse);
                float contactSpeed = Mathf.Abs(Vector2.Dot(contact.relativeVelocity, contact.normal));
                // Contact velocity may already reflect the native solver. Retain the
                // incoming point speed, including rotation, for resting-surface impacts.
                if (motionCaptured)
                {
                    Vector2 offset = contact.point - beforeCenter;
                    Vector2 incoming = beforeVelocity + new Vector2(-offset.y, offset.x) * beforeSpin;
                    Vector2 other = collision.rigidbody != null ? collision.rigidbody.GetPointVelocity(contact.point) : Vector2.zero;
                    contactSpeed = Mathf.Max(contactSpeed, Mathf.Abs(Vector2.Dot(incoming - other, contact.normal)));
                }
                if (contactSpeed >= speed) { speed = contactSpeed; point = contact.point; }
            }
            QueueImpact(speed, impulse, point);
        }
        private void QueueImpact(float speed, float impulse, Vector2 point)
        {
            if (!CanBreak || breakQueued) return;
            LastImpactSpeed = speed; LastImpactImpulse = impulse; LastImpactPoint = point;
            if (speed < profile.minimumImpactSpeed
                || impulse < Body.Body.mass * profile.minimumImpulsePerMass) return;
            breakQueued = true;
            world.QueueGlassBreakage(this);
        }

        internal void CompleteBreak()
        {
            if (!breakQueued || !CanBreak) { breakQueued = false; return; }
            // Validate the complete authored set before removing the intact vessel.
            float totalArea = 0;
            Vector2 weightedCenter = Vector2.zero;
            foreach (var shard in profile.shards)
            {
                if (shard == null || shard.sprite == null || shard.outline == null || shard.outline.Length < 3
                    || !float.IsFinite(shard.area) || shard.area <= 0)
                { breakQueued = false; Debug.LogError("Invalid glass shard profile: " + name, this); return; }
                totalArea += shard.area; weightedCenter += shard.localCenter * shard.area;
            }
            Vector2 center = Body.LocalToWorld(weightedCenter / totalArea);
            // Create disabled native bodies while the original pose/velocity is available.
            // Enable them only after the intact colliders and GPU owner are removed.
            var fragments = new FluidExperimentGlassShard[profile.shards.Length];
            for (int i = 0; i < fragments.Length; i++)
                fragments[i] = FluidExperimentGlassShard.Create(this, world, i, totalArea, center);
            IsBroken = true; breakQueued = false;
            gameObject.SetActive(false); // Body.OnDisable unregisters input, contents and liquid ownership.
            foreach (var shard in fragments) shard.Activate();
            world.RefreshGlassShardContacts();
        }

        internal void RestoreIntact()
        {
            breakQueued = motionCaptured = false;
            LastImpactSpeed = LastImpactImpulse = 0;
            if (!IsBroken) return;
            IsBroken = false;
            gameObject.SetActive(true);
        }
    }
}
