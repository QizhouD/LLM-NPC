using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class AgentInteraction : MonoBehaviour
    {
        // Close enough to stop next to an object rather than at conversational distance.
        private const float InteractionStoppingDistance = 0.02f;
        private const float LiftClearance = 0.06f;
        private const float FaceAngleTolerance = 2f;

        [SerializeField] private AgentMovement movement;
        [Tooltip("Holds a picked-up item on rigs CharacterIKLayer cannot drive.")]
        [SerializeField] private Transform rightHand;
        [Tooltip("Animates picking up, carrying and setting down with both hands. " +
                 "Without it items snap to rightHand.")]
        [SerializeField] private CharacterIKLayer ikLayer;
        [SerializeField, Min(0.2f)] private float interactionDistance = 0.78f;
        [SerializeField, Min(1f)] private float faceTurnSpeed = 540f;
        [SerializeField, Min(0.2f)] private float dropDistance = 0.8f;
        [SerializeField] private Vector3 heldLocalPosition =
            new Vector3(0f, -0.10f, 0.18f);
        [SerializeField] private Vector3 heldLocalEulerAngles =
            new Vector3(0f, 20f, 0f);

        [Header("Two-handed carry")]
        [Tooltip("Metres from the body's centre to the near side of an item being picked up " +
                 "or set down on the floor.")]
        [SerializeField, Range(0.05f, 0.6f)] private float reachStandOff = 0.2f;
        [Tooltip("Height in metres of a carried item's centre above the feet.")]
        [SerializeField, Range(0.6f, 1.6f)] private float carryHeight = 1.15f;
        [Tooltip("Metres from the body's centre to the near side of a carried item.")]
        [SerializeField, Range(0.05f, 0.5f)] private float carryStandOff = 0.16f;
        [Tooltip("Metres from a surface's edge at which to stop and lift the item over it.")]
        [SerializeField, Range(0.3f, 1.5f)] private float surfaceStagingDistance = 0.8f;
        [Tooltip("Metres from a surface's edge to step in to before setting the item on it.")]
        [SerializeField, Range(0.1f, 0.8f)] private float surfaceReachDistance = 0.3f;
        [SerializeField, Min(0.1f)] private float reachDuration = 0.7f;
        [SerializeField, Min(0.1f)] private float liftDuration = 0.65f;

        public GameObject CurrentTarget { get; private set; }
        public GameObject HeldItem { get; private set; }
        public Transform RightHand => rightHand;

        /// <summary>Outcome of the last pickup, drop or place routine.</summary>
        public bool LastActionSucceeded { get; private set; }

        private bool CanAnimate =>
            ikLayer != null && ikLayer.isActiveAndEnabled && ikLayer.IsReady;

        private void Awake()
        {
            if (ikLayer == null)
            {
                ikLayer = GetComponent<CharacterIKLayer>();
            }
        }

        public void Configure(AgentMovement agentMovement, Transform hand)
        {
            movement = agentMovement;
            rightHand = hand;
        }

        public bool MoveToTarget(GameObject target)
        {
            if (target == null || movement == null)
            {
                return false;
            }

            CurrentTarget = target;
            Vector3 approach = CalculateApproachPoint(target);
            bool closeUp = target.GetComponent<PickupItem>() != null ||
                           target.GetComponent<PlaceSurface>() != null;
            return closeUp
                ? movement.MoveTo(approach, InteractionStoppingDistance)
                : movement.MoveTo(approach);
        }

        public Vector3 CalculateApproachPoint(GameObject target)
        {
            PlaceSurface surface = target.GetComponent<PlaceSurface>();
            if (surface != null)
            {
                return SurfaceApproach(surface.GetWorldBounds(), surfaceStagingDistance, out _);
            }

            PickupItem item = target.GetComponent<PickupItem>();
            if (item != null && !item.IsPickedUp)
            {
                return ItemApproach(target.transform);
            }

            Vector3 targetPosition = target.transform.position;
            Collider targetCollider = target.GetComponent<Collider>();
            float clearance = interactionDistance;
            if (targetCollider != null)
            {
                Vector3 extents = targetCollider.bounds.extents;
                clearance = Mathf.Max(extents.x, extents.z) + 0.45f;
            }

            Vector3 awayFromTarget = transform.position - targetPosition;
            awayFromTarget.y = 0f;
            if (awayFromTarget.sqrMagnitude < 0.01f)
            {
                awayFromTarget = -target.transform.forward;
            }

            Vector3 approach = targetPosition +
                               awayFromTarget.normalized * clearance;
            approach.y = transform.position.y;
            return approach;
        }

        public IEnumerator FaceTarget(GameObject target)
        {
            if (target == null)
            {
                yield break;
            }

            yield return FaceDirection(target.transform.position - transform.position);
        }

        public bool Pickup(GameObject target)
        {
            if (target == null || rightHand == null)
            {
                return false;
            }

            PickupItem item = target.GetComponent<PickupItem>();
            if (item == null || item.InteractableType != AgentInteractableType.Pickup)
            {
                return false;
            }

            if (HeldItem != null && HeldItem != target)
            {
                DropInFront();
            }

            NotifyRemovedFromSurfaces(target);
            item.CaptureOriginalTransform();
            FreezeBody(target);
            SetCollidersEnabled(target, false);

            // Keeping the world scale through the reparent is what stops the item resizing.
            target.transform.SetParent(rightHand, true);
            target.transform.localPosition = heldLocalPosition;
            target.transform.localRotation = Quaternion.Euler(heldLocalEulerAngles);
            item.MarkPickedUp(rightHand);
            HeldItem = target;
            CurrentTarget = null;
            Debug.Log($"[AgentNPC] Picked up {target.name}.", this);
            return true;
        }

        public bool DropInFront()
        {
            if (HeldItem == null)
            {
                return false;
            }

            PickupItem item = HeldItem.GetComponent<PickupItem>();
            Vector3 dropPoint = transform.position + transform.forward * dropDistance;
            if (NavMesh.SamplePosition(
                    dropPoint,
                    out NavMeshHit hit,
                    1.8f,
                    NavMesh.AllAreas))
            {
                dropPoint = hit.position;
            }
            else
            {
                dropPoint.y = transform.position.y;
            }

            dropPoint.y += HalfSize(HeldItem.transform).y;
            ReleaseItem(HeldItem, dropPoint, UprightRotation(HeldItem.transform), item != null
                ? item.OriginalParent
                : null);
            return true;
        }

        public bool PlaceHeldItemOn(PlaceSurface surface)
        {
            if (HeldItem == null || surface == null)
            {
                return false;
            }

            Vector3 placePoint = surface.ReservePlacePosition(
                HeldItem,
                HalfSize(HeldItem.transform) * 2f,
                transform.position);
            ReleaseItem(HeldItem, placePoint, UprightRotation(HeldItem.transform), null);
            Debug.Log(
                $"[AgentNPC] Placed item on {surface.DisplayName} at {placePoint}.",
                this);
            return true;
        }

        /// <summary>
        /// Crouches, takes <paramref name="target"/> in both hands and stands up with it held
        /// in front of the chest. Expects the body to be standing at
        /// <see cref="CalculateApproachPoint"/> and facing the item. Sets
        /// <see cref="LastActionSucceeded"/>.
        /// </summary>
        public IEnumerator PickupRoutine(GameObject target)
        {
            LastActionSucceeded = false;
            PickupItem item = target != null ? target.GetComponent<PickupItem>() : null;
            if (item == null || item.InteractableType != AgentInteractableType.Pickup)
            {
                yield break;
            }

            if (!CanAnimate)
            {
                LastActionSucceeded = Pickup(target);
                yield break;
            }

            if (HeldItem != null && HeldItem != target)
            {
                yield return DropRoutine();
            }

            NotifyRemovedFromSurfaces(target);
            item.CaptureOriginalTransform();
            FreezeBody(target);

            ikLayer.BeginHold(target.transform);
            float crouch = ikLayer.CrouchToReachHold();
            yield return Blend(reachDuration, t =>
            {
                ikLayer.CrouchAmount = crouch * t;
                ikLayer.HoldWeight = t;
            });
            yield return new WaitForSeconds(0.1f);

            SetCollidersEnabled(target, false);
            Transform anchor = ikLayer.CarryAnchor;
            target.transform.SetParent(anchor, true);
            item.MarkPickedUp(anchor);
            HeldItem = target;
            CurrentTarget = null;

            Transform carried = target.transform;
            Vector3 fromPosition = carried.localPosition;
            Quaternion fromRotation = carried.localRotation;
            Quaternion toRotation = SquareToBody(fromRotation);
            Vector3 toPosition = CarryPosition(carried, toRotation);
            yield return Blend(liftDuration, t =>
            {
                carried.localPosition = Vector3.Lerp(fromPosition, toPosition, t);
                carried.localRotation = Quaternion.Slerp(fromRotation, toRotation, t);
                ikLayer.CrouchAmount = crouch * (1f - t);
            });

            LastActionSucceeded = true;
            Debug.Log($"[AgentNPC] Picked up {target.name}.", this);
        }

        /// <summary>
        /// Crouches and sets the held item down on the floor in front, then stands up.
        /// Falls back to <see cref="DropInFront"/> when that spot is taken.
        /// </summary>
        public IEnumerator DropRoutine()
        {
            LastActionSucceeded = false;
            GameObject held = HeldItem;
            if (held == null)
            {
                yield break;
            }

            if (!CanAnimate || !TryFindDropSpot(held.transform, out Vector3 spot))
            {
                LastActionSucceeded = DropInFront();
                ikLayer?.EndHold();
                yield break;
            }

            PickupItem item = held.GetComponent<PickupItem>();
            Transform carried = held.transform;
            carried.SetParent(item != null ? item.OriginalParent : null, true);
            Vector3 fromPosition = carried.position;
            Quaternion rotation = carried.rotation;
            float crouch = CrouchToReachAt(carried, spot);

            yield return Blend(liftDuration, t =>
            {
                carried.position = Vector3.Lerp(fromPosition, spot, t);
                ikLayer.CrouchAmount = crouch * t;
            });

            ReleaseItem(held, spot, rotation, item != null ? item.OriginalParent : null);
            yield return LetGo(crouch, 0f);
            LastActionSucceeded = true;
        }

        /// <summary>
        /// Lifts the held item over the near edge of <paramref name="surface"/>, steps in,
        /// pushes it onto the top and lets go. Expects the body to be standing at
        /// <see cref="CalculateApproachPoint"/> for the surface.
        /// </summary>
        public IEnumerator PlaceRoutine(PlaceSurface surface)
        {
            LastActionSucceeded = false;
            GameObject held = HeldItem;
            if (held == null || surface == null)
            {
                yield break;
            }

            Bounds bounds = surface.GetWorldBounds();
            SurfaceApproach(bounds, 0f, out Vector3 outward);
            yield return FaceDirection(-outward);
            if (!CanAnimate)
            {
                LastActionSucceeded = PlaceHeldItemOn(surface);
                yield break;
            }

            Transform carried = held.transform;
            Vector3 size = HalfSize(carried) * 2f;
            Vector3 carriedPosition = carried.localPosition;
            float clearHeight = bounds.max.y - transform.position.y + size.y * 0.5f + LiftClearance;
            if (clearHeight > carriedPosition.y)
            {
                Vector3 raised = new Vector3(carriedPosition.x, clearHeight, carriedPosition.z);
                yield return Blend(liftDuration, t =>
                    carried.localPosition = Vector3.Lerp(carriedPosition, raised, t));
            }

            Vector3 edge = SurfaceApproach(bounds, surfaceReachDistance, out _);
            if (movement != null && movement.MoveTo(edge, InteractionStoppingDistance))
            {
                float deadline = Time.time + 4f;
                while (movement.IsMoving && Time.time < deadline)
                {
                    yield return null;
                }

                movement.Stop();
            }

            yield return FaceDirection(-outward);

            Vector3 rest = surface.ReservePlacePosition(held, size, transform.position);
            carried.SetParent(null, true);
            Vector3 fromPosition = carried.position;
            Vector3 over = new Vector3(rest.x, Mathf.Max(fromPosition.y, rest.y + LiftClearance), rest.z);
            float reachUp = rest.y - transform.position.y > carryHeight ? 1f : 0f;
            float crouch = CrouchToReachAt(carried, rest);

            yield return Blend(liftDuration, t =>
            {
                carried.position = Vector3.Lerp(fromPosition, over, t);
                ikLayer.ReachUpAmount = reachUp * t;
                ikLayer.CrouchAmount = crouch * t;
            });
            yield return Blend(0.25f, t => carried.position = Vector3.Lerp(over, rest, t));

            ReleaseItem(held, rest, carried.rotation, null);
            yield return LetGo(crouch, reachUp);
            LastActionSucceeded = true;
            Debug.Log($"[AgentNPC] Placed item on {surface.DisplayName} at {rest}.", this);
        }

        /// <summary>
        /// Settles the body after a pickup, drop or place routine was stopped part way:
        /// stands up, keeps an item that is already being carried, and puts down one that
        /// was on its way to the floor or a surface.
        /// </summary>
        public void CancelAction()
        {
            if (ikLayer == null)
            {
                return;
            }

            ikLayer.CrouchAmount = 0f;
            ikLayer.ReachUpAmount = 0f;
            if (HeldItem != null && HeldItem.transform.parent != ikLayer.CarryAnchor &&
                HeldItem.transform.parent != rightHand)
            {
                DropInFront();
            }

            if (HeldItem == null)
            {
                ikLayer.EndHold();
            }
        }

        private IEnumerator LetGo(float crouch, float reachUp)
        {
            yield return new WaitForSeconds(0.08f);
            yield return Blend(reachDuration, t =>
            {
                ikLayer.HoldWeight = 1f - t;
                ikLayer.CrouchAmount = crouch * (1f - t);
                ikLayer.ReachUpAmount = reachUp * (1f - t);
            });
            ikLayer.EndHold();
        }

        private IEnumerator FaceDirection(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                yield break;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float deadline = Time.time + 1.5f;
            while (Time.time < deadline &&
                   Quaternion.Angle(transform.rotation, targetRotation) > FaceAngleTolerance)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    faceTurnSpeed * Time.deltaTime);
                yield return null;
            }

            transform.rotation = targetRotation;
        }

        private static IEnumerator Blend(float duration, Action<float> apply)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                apply(Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
                elapsed += Time.deltaTime;
            }

            apply(1f);
        }

        /// <summary>Crouch the hands need to reach <paramref name="item"/> once it sits at <paramref name="centre"/>.</summary>
        private float CrouchToReachAt(Transform item, Vector3 centre)
        {
            Vector3 current = item.position;
            item.position = centre;
            float crouch = ikLayer.CrouchToReachHold();
            item.position = current;
            return crouch;
        }

        /// <summary>
        /// Standing point square on to the face of <paramref name="item"/> that looks most
        /// nearly towards the character, close enough to reach it from a crouch.
        /// </summary>
        private Vector3 ItemApproach(Transform item)
        {
            Vector3 toAgent = transform.position - item.position;
            toAgent.y = 0f;
            if (toAgent.sqrMagnitude < 0.0001f)
            {
                toAgent = -item.forward;
            }

            toAgent.Normalize();
            Vector3 face = Vector3.zero;
            float best = float.MinValue;
            foreach (Vector3 axis in new[] { item.right, -item.right, item.forward, -item.forward })
            {
                Vector3 flat = new Vector3(axis.x, 0f, axis.z).normalized;
                float alignment = Vector3.Dot(flat, toAgent);
                if (alignment > best)
                {
                    best = alignment;
                    face = flat;
                }
            }

            Vector3 approach = item.position + face * (HalfExtentAlong(item, face) + reachStandOff);
            approach.y = transform.position.y;
            return approach;
        }

        /// <summary>
        /// Point <paramref name="distance"/> out from the side of <paramref name="bounds"/>
        /// nearest the character, and that side's outward normal.
        /// </summary>
        private Vector3 SurfaceApproach(Bounds bounds, float distance, out Vector3 outward)
        {
            Vector3 position = transform.position;
            float outsideX = Mathf.Max(bounds.min.x - position.x, position.x - bounds.max.x);
            float outsideZ = Mathf.Max(bounds.min.z - position.z, position.z - bounds.max.z);
            if (outsideX >= outsideZ)
            {
                outward = position.x < bounds.center.x ? Vector3.left : Vector3.right;
            }
            else
            {
                outward = position.z < bounds.center.z ? Vector3.back : Vector3.forward;
            }

            // Stay clear of the corners so the item fits on the top in front of the body.
            const float cornerMargin = 0.35f;
            float x = outward.x != 0f
                ? (outward.x < 0f ? bounds.min.x : bounds.max.x)
                : ClampAlongSide(position.x, bounds.min.x, bounds.max.x, cornerMargin);
            float z = outward.z != 0f
                ? (outward.z < 0f ? bounds.min.z : bounds.max.z)
                : ClampAlongSide(position.z, bounds.min.z, bounds.max.z, cornerMargin);
            return new Vector3(x, position.y, z) + outward * distance;
        }

        private static float ClampAlongSide(float value, float min, float max, float margin)
        {
            return min + margin > max - margin
                ? (min + max) * 0.5f
                : Mathf.Clamp(value, min + margin, max - margin);
        }

        /// <summary>
        /// Floor spot in front of the body for the held item, or false when the floor is
        /// missing, much higher than the feet, or already occupied there.
        /// </summary>
        private bool TryFindDropSpot(Transform item, out Vector3 centre)
        {
            Vector3 half = HalfSize(item);
            Vector3 forward = transform.forward;
            Vector3 spot = transform.position +
                           forward * (reachStandOff + HalfExtentAlong(item, forward));
            centre = spot;

            Ray down = new Ray(spot + Vector3.up * 2f, Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(down, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float floor = float.MinValue;
            foreach (RaycastHit hit in hits)
            {
                if (!hit.collider.transform.IsChildOf(transform) &&
                    !hit.collider.transform.IsChildOf(item))
                {
                    floor = Mathf.Max(floor, hit.point.y);
                }
            }

            if (floor == float.MinValue || floor - transform.position.y > 0.25f)
            {
                return false;
            }

            centre = new Vector3(spot.x, floor + half.y, spot.z);
            Collider[] overlaps = Physics.OverlapBox(
                centre + Vector3.up * 0.01f,
                half * 0.95f,
                item.rotation,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
            {
                if (!overlap.transform.IsChildOf(transform) && !overlap.transform.IsChildOf(item))
                {
                    return false;
                }
            }

            return true;
        }

        private Vector3 CarryPosition(Transform item, Quaternion localRotation)
        {
            Vector3 half = HalfSize(item);
            float halfDepth = Mathf.Abs((localRotation * Vector3.right).z) * half.x +
                              Mathf.Abs((localRotation * Vector3.up).z) * half.y +
                              Mathf.Abs((localRotation * Vector3.forward).z) * half.z;
            return new Vector3(0f, carryHeight, carryStandOff + halfDepth) -
                   localRotation * CentreOffset(item);
        }

        /// <summary>Yaw relative to the body, turned to the nearest quarter so a face points forward.</summary>
        private static Quaternion SquareToBody(Quaternion localRotation)
        {
            Vector3 forward = localRotation * Vector3.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f);
        }

        private static Quaternion UprightRotation(Transform item)
        {
            Vector3 forward = Vector3.ProjectOnPlane(item.forward, Vector3.up);
            return forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
        }

        /// <summary>Half the item's box size along its own axes, in world units.</summary>
        private static Vector3 HalfSize(Transform item)
        {
            BoxCollider box = item.GetComponent<BoxCollider>();
            Vector3 size = box != null ? box.size : Vector3.one;
            return Vector3.Scale(size, item.lossyScale) * 0.5f;
        }

        private static Vector3 CentreOffset(Transform item)
        {
            BoxCollider box = item.GetComponent<BoxCollider>();
            return box != null ? Vector3.Scale(box.center, item.lossyScale) : Vector3.zero;
        }

        private static float HalfExtentAlong(Transform item, Vector3 direction)
        {
            Vector3 half = HalfSize(item);
            return Mathf.Abs(Vector3.Dot(item.right, direction)) * half.x +
                   Mathf.Abs(Vector3.Dot(item.up, direction)) * half.y +
                   Mathf.Abs(Vector3.Dot(item.forward, direction)) * half.z;
        }

        private static void FreezeBody(GameObject target)
        {
            Rigidbody body = target.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        private void ReleaseItem(
            GameObject item,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Transform worldParent)
        {
            item.transform.SetParent(worldParent, true);
            item.transform.SetPositionAndRotation(worldPosition, worldRotation);
            FreezeBody(item);
            SetCollidersEnabled(item, true);
            item.GetComponent<PickupItem>()?.MarkReleased();
            if (HeldItem == item)
            {
                HeldItem = null;
            }

            CurrentTarget = null;
            Debug.Log($"[AgentNPC] Released {item.name} at {worldPosition}.", this);
        }

        private static void NotifyRemovedFromSurfaces(GameObject item)
        {
            PlaceSurface[] surfaces = UnityEngine.Object.FindObjectsByType<PlaceSurface>(
                FindObjectsSortMode.None);
            for (int i = 0; i < surfaces.Length; i++)
            {
                surfaces[i].NotifyRemoved(item);
            }
        }

        private static void SetCollidersEnabled(GameObject target, bool enabled)
        {
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = enabled;
            }
        }

        private void OnValidate()
        {
            interactionDistance = Mathf.Max(0.2f, interactionDistance);
            faceTurnSpeed = Mathf.Max(1f, faceTurnSpeed);
            dropDistance = Mathf.Max(0.2f, dropDistance);
        }
    }
}
