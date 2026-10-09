using UnityEngine;

namespace Slainte.Bartending.FluidGpuExperiment
{
    public enum FluidExperimentShakerPartRole { Strainer, Cap }

    // Input is owned exclusively by FluidExperimentInteractor. This component
    // supplies a detachable physical part without legacy selection or slot state.
    [DisallowMultipleComponent]
    public sealed class FluidExperimentShakerPart : MonoBehaviour
    {
        public FluidExperimentBody Owner { get; private set; }
        public FluidExperimentShakerPartRole Role { get; private set; }
        public bool IsAttached { get; private set; }
        public bool IsHeld { get; private set; }
        // Attached parts follow the carrier's completed physics pose. Transform interpolation
        // can still be displaying the previous frame when input detaches or moves the carrier.
        public Vector2 Position => IsAttached && Owner != null
            ? Role == FluidExperimentShakerPartRole.Strainer
                ? Owner.LocalToWorld(HomeLocalPosition)
                : Owner.ShakerStrainer.PartPoint(HomeLocalPosition - Owner.ShakerStrainer.HomeLocalPosition)
            : body != null && body.simulated ? body.position : (Vector2)transform.position;
        public float Angle => IsAttached && Owner != null
            ? Role == FluidExperimentShakerPartRole.Strainer ? Owner.Angle : Owner.ShakerStrainer.Angle
            : body != null ? body.rotation : transform.eulerAngles.z;
        public Vector2 HomeLocalPosition { get; private set; }
        public Rigidbody2D Body => body;
        public BoxCollider2D SolidCollider => solid;
        private Rigidbody2D body;
        private BoxCollider2D solid;
        private BoxCollider2D carriedCapSolid;
        private Vector2 pointerOffset;
        private Rect hitRect;

        internal static FluidExperimentShakerPart Create(FluidExperimentBody owner,
            FluidExperimentShakerPartRole role, SpriteRenderer art, Rect ownerLocalRect)
        {
            var root = new GameObject("Shaker" + role);
            root.layer = owner.gameObject.layer;
            root.transform.SetParent(owner.transform, false);
            root.transform.localPosition = ownerLocalRect.center;
            if (art != null) art.transform.SetParent(root.transform, true);
            var part = root.AddComponent<FluidExperimentShakerPart>();
            part.Owner = owner;
            part.Role = role;
            part.HomeLocalPosition = ownerLocalRect.center;
            part.hitRect = new Rect(-ownerLocalRect.size * .5f, ownerLocalRect.size);
            part.body = root.AddComponent<Rigidbody2D>();
            part.body.mass = role == FluidExperimentShakerPartRole.Cap ? .03f : .08f;
            part.body.interpolation = RigidbodyInterpolation2D.Interpolate;
            part.body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            part.body.simulated = false;
            part.solid = root.AddComponent<BoxCollider2D>();
            part.solid.size = ownerLocalRect.size;
            part.solid.enabled = false;
            if (owner.solidColliders.Length > 0 && owner.solidColliders[0] != null)
                part.solid.sharedMaterial = owner.solidColliders[0].sharedMaterial;
            if (role == FluidExperimentShakerPartRole.Strainer)
            {
                part.carriedCapSolid = root.AddComponent<BoxCollider2D>();
                part.carriedCapSolid.sharedMaterial = part.solid.sharedMaterial;
                part.carriedCapSolid.enabled = false;
            }
            return part;
        }

        public bool ContainsWorldPoint(Vector2 worldPoint)
        {
            Vector2 local = FluidExperimentBody.Rotate(worldPoint - Position, -Angle);
            Vector3 scale = transform.lossyScale;
            local = new Vector2(local.x / scale.x, local.y / scale.y);
            return isActiveAndEnabled && Owner != null && Owner.isActiveAndEnabled
                && hitRect.Contains(local);
        }

        private Vector2 PartPoint(Vector2 local)
        {
            Vector3 scale = transform.lossyScale;
            return Position + FluidExperimentBody.Rotate(new Vector2(local.x * scale.x, local.y * scale.y), Angle);
        }

        public bool TryPickUp(Vector2 pointer)
        {
            if (Owner == null || Owner.IsHeld || IsHeld || !Owner.isActiveAndEnabled) return false;
            if (Role == FluidExperimentShakerPartRole.Cap
                && Owner.ShakerStrainer != null && Owner.ShakerStrainer.IsHeld) return false;
            Detach();
            IsHeld = true;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0;
            pointerOffset = Position - pointer;
            RefreshCollisionPairs();
            return true;
        }

        public void MoveToPointer(Vector2 pointer)
        {
            if (!IsHeld) return;
            SetWorldPose(pointer + pointerOffset, body.rotation);
        }

        internal void RebasePointer(Vector2 pointer)
        {
            if (IsHeld) pointerOffset = Position - pointer;
        }

        private void SetWorldPose(Vector2 position, float rotation)
        {
            // A carried cap has its own disabled Rigidbody. Synchronize the carrier's
            // Transform too so a child reparent/SyncTransforms cannot restore an old pose.
            transform.SetPositionAndRotation(new Vector3(position.x, position.y, transform.position.z), Quaternion.Euler(0, 0, rotation));
            body.position = position;
            body.rotation = rotation;
        }

        private void Detach()
        {
            if (!IsAttached) return;
            Vector2 position = Position;
            float rotation = Angle;
            transform.SetParent(Owner.transform.parent, true);
            IsAttached = false;
            transform.SetPositionAndRotation(new Vector3(position.x, position.y, transform.position.z), Quaternion.Euler(0, 0, rotation));
            body.bodyType = RigidbodyType2D.Dynamic;
            body.simulated = true;
            body.position = position;
            body.rotation = rotation;
            solid.enabled = true;
            RefreshCarrierCollider();
            Owner.RefreshShakerClosure();
        }

        public bool TryAttach()
        {
            if (Owner == null || Owner.IsHeld || IsAttached) return false;
            Transform carrier;
            Vector2 localPosition;
            if (Role == FluidExperimentShakerPartRole.Strainer)
            {
                carrier = Owner.transform;
                localPosition = HomeLocalPosition;
            }
            else
            {
                if (Owner.ShakerStrainer == null || Owner.ShakerStrainer.IsHeld) return false;
                carrier = Owner.ShakerStrainer.transform;
                localPosition = HomeLocalPosition - Owner.ShakerStrainer.HomeLocalPosition;
            }
            Vector2 target = Role == FluidExperimentShakerPartRole.Strainer
                ? Owner.LocalToWorld(localPosition) : Owner.ShakerStrainer.PartPoint(localPosition);
            float scale = Mathf.Max(Mathf.Abs(carrier.lossyScale.x), Mathf.Abs(carrier.lossyScale.y));
            if (Vector2.Distance(Position, target) > .28f * scale) return false;
            if (Role == FluidExperimentShakerPartRole.Strainer) AttachToBody();
            else AttachToStrainer();
            Owner.RefreshShakerClosure();
            return true;
        }

        internal void AttachToBody()
        {
            Owner.World?.RefreshIceContainment();
            Attach(Owner.transform, HomeLocalPosition);
        }

        internal void AttachToStrainer()
        {
            if (Owner.ShakerStrainer == null) return;
            Attach(Owner.ShakerStrainer.transform, HomeLocalPosition - Owner.ShakerStrainer.HomeLocalPosition);
        }

        private void Attach(Transform parent, Vector2 localPosition)
        {
            IsHeld = false;
            IsAttached = true;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0;
            body.simulated = false;
            solid.enabled = false;
            if (carriedCapSolid != null) carriedCapSolid.enabled = false;
            transform.SetParent(parent, false);
            transform.localPosition = localPosition;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        internal void ParkBesideOwner(Vector2 offset)
        {
            Detach();
            SetWorldPose(Owner.LocalToWorld(HomeLocalPosition + offset), Owner.Angle);
            Release(Vector2.zero);
        }

        public void Release(Vector2 velocity)
        {
            if (IsAttached) return;
            IsHeld = false;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.simulated = true;
            body.linearVelocity = velocity;
            body.angularVelocity = 0;
            body.WakeUp();
            RefreshCollisionPairs();
        }

        internal void RefreshCarrierCollider()
        {
            if (carriedCapSolid == null) return;
            FluidExperimentShakerPart cap = Owner != null ? Owner.ShakerCap : null;
            bool carriesCap = cap != null && cap.IsAttached && cap.transform.parent == transform;
            carriedCapSolid.enabled = !IsAttached && carriesCap;
            if (!carriesCap) return;
            carriedCapSolid.offset = cap.HomeLocalPosition - HomeLocalPosition;
            carriedCapSolid.size = cap.solid.size;
        }

        private void FixedUpdate()
        {
            if (!IsAttached) RefreshCollisionPairs();
        }

        private void RefreshCollisionPairs()
        {
            if (Owner == null || Owner.World == null || IsAttached) return;
            FluidExperimentWorld world = Owner.World;
            foreach (FluidExperimentBody item in world.Items)
            {
                if (item == null) continue;
                foreach (Collider2D other in item.solidColliders)
                {
                    if (other == null || !other.enabled) continue;
                    Ignore(other, IsHeld || item.IsHeld || item.IsInHeldVessel);
                }
                if (item.kind == LabItemKind.Shaker)
                {
                    PairWithPart(item.ShakerCap);
                    PairWithPart(item.ShakerStrainer);
                }
            }
            foreach (Collider2D floor in world.floorColliders)
                if (floor != null && floor.enabled) Ignore(floor, IsHeld);
        }

        private void PairWithPart(FluidExperimentShakerPart other)
        {
            if (other == null || other == this || other.IsAttached) return;
            Ignore(other.solid, IsHeld || other.IsHeld);
            if (other.carriedCapSolid != null && other.carriedCapSolid.enabled)
                Ignore(other.carriedCapSolid, IsHeld || other.IsHeld);
        }

        private void Ignore(Collider2D other, bool ignore)
        {
            if (solid.enabled) Physics2D.IgnoreCollision(solid, other, ignore);
            if (carriedCapSolid != null && carriedCapSolid.enabled)
                Physics2D.IgnoreCollision(carriedCapSolid, other, ignore);
        }

        private void OnDisable()
        {
            Owner?.World?.interactor?.ForgetShakerPart(this);
        }
    }
}
