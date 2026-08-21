using System.Collections;
using UnityEngine;
using UnityEngine.AI;

namespace DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class AgentInteraction : MonoBehaviour
    {
        [SerializeField] private AgentMovement movement;
        [SerializeField] private Transform rightHand;
        [SerializeField, Min(0.2f)] private float interactionDistance = 0.78f;
        [SerializeField, Min(1f)] private float faceTurnSpeed = 540f;
        [SerializeField, Min(0.2f)] private float dropDistance = 0.8f;
        [SerializeField] private Vector3 heldLocalPosition =
            new Vector3(0f, -0.10f, 0.18f);
        [SerializeField] private Vector3 heldLocalEulerAngles =
            new Vector3(0f, 20f, 0f);
        [SerializeField] private Vector3 heldLocalScale =
            new Vector3(0.28f, 0.28f, 0.28f);

        public GameObject CurrentTarget { get; private set; }
        public GameObject HeldItem { get; private set; }
        public Transform RightHand => rightHand;

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
            return movement.MoveTo(CalculateApproachPoint(target));
        }

        public Vector3 CalculateApproachPoint(GameObject target)
        {
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

            const float angleTolerance = 2f;
            float deadline = Time.time + 1.5f;
            while (Time.time < deadline)
            {
                Vector3 direction = target.transform.position - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    yield break;
                }

                Quaternion targetRotation = Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up);
                if (Quaternion.Angle(transform.rotation, targetRotation) <=
                    angleTolerance)
                {
                    transform.rotation = targetRotation;
                    yield break;
                }

                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    faceTurnSpeed * Time.deltaTime);
                yield return null;
            }
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

            Rigidbody body = target.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            SetCollidersEnabled(target, false);
            target.transform.SetParent(rightHand, false);
            target.transform.localPosition = heldLocalPosition;
            target.transform.localRotation = Quaternion.Euler(heldLocalEulerAngles);
            target.transform.localScale = heldLocalScale;
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
            Vector3 scale = item != null ? item.OriginalScale : Vector3.one * 0.7f;
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

            dropPoint.y += scale.y * 0.5f;
            ReleaseItem(HeldItem, dropPoint, Quaternion.identity, item != null
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

            PickupItem item = HeldItem.GetComponent<PickupItem>();
            Vector3 scale = item != null ? item.OriginalScale : Vector3.one * 0.7f;
            Vector3 placePoint = surface.ReservePlacePosition(HeldItem, scale);
            ReleaseItem(HeldItem, placePoint, Quaternion.identity, null);
            Debug.Log(
                $"[AgentNPC] Placed item on {surface.DisplayName} at {placePoint}.",
                this);
            return true;
        }

        private void ReleaseItem(
            GameObject item,
            Vector3 worldPosition,
            Quaternion worldRotation,
            Transform worldParent)
        {
            item.transform.SetParent(worldParent, true);
            item.transform.position = worldPosition;
            item.transform.rotation = worldRotation;
            PickupItem pickup = item.GetComponent<PickupItem>();
            item.transform.localScale = pickup != null
                ? pickup.OriginalScale
                : Vector3.one * 0.7f;

            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            SetCollidersEnabled(item, true);
            pickup?.MarkReleased();
            if (HeldItem == item)
            {
                HeldItem = null;
            }

            CurrentTarget = null;
            Debug.Log($"[AgentNPC] Released {item.name} at {worldPosition}.", this);
        }

        private static void NotifyRemovedFromSurfaces(GameObject item)
        {
            PlaceSurface[] surfaces = Object.FindObjectsByType<PlaceSurface>(
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
