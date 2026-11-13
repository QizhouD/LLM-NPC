using UnityEngine;
using UnityEngine.AI;

namespace DigitalHuman
{
    public enum AgentInteractableType
    {
        Pickup
    }

    [DisallowMultipleComponent]
    public sealed class PickupItem : MonoBehaviour
    {
        [SerializeField] private AgentInteractableType interactableType =
            AgentInteractableType.Pickup;
        [SerializeField] private string displayName = "Item";
        [SerializeField] private string colorName = "Unknown";

        public AgentInteractableType InteractableType => interactableType;
        public string DisplayName => displayName;
        public string ColorName => colorName;
        public bool IsPickedUp { get; private set; }
        public Transform Holder { get; private set; }
        public Vector3 OriginalScale { get; private set; } = Vector3.one * 0.7f;
        public Transform OriginalParent { get; private set; }

        private bool capturedOriginalTransform;

        private void Awake()
        {
            CaptureOriginalTransform();
        }

        public void Configure(string itemDisplayName, string itemColorName)
        {
            interactableType = AgentInteractableType.Pickup;
            displayName = string.IsNullOrWhiteSpace(itemDisplayName)
                ? gameObject.name
                : itemDisplayName;
            colorName = itemColorName ?? string.Empty;
            CaptureOriginalTransform();
        }

        public void CaptureOriginalTransform()
        {
            if (capturedOriginalTransform)
            {
                return;
            }

            OriginalScale = transform.localScale;
            OriginalParent = transform.parent;
            capturedOriginalTransform = true;
        }

        public void MarkPickedUp(Transform holder)
        {
            IsPickedUp = holder != null;
            Holder = holder;
            // A carried item must not carve the NavMesh: it would cut a hole that follows
            // the character around, under its own feet.
            SetNavMeshCarving(!IsPickedUp);
        }

        public void MarkReleased()
        {
            IsPickedUp = false;
            Holder = null;
            SetNavMeshCarving(true);
        }

        /// <summary>
        /// Enables or disables the item's <see cref="NavMeshObstacle"/>, if it has one, so
        /// routes go around it only while it rests in the world.
        /// </summary>
        private void SetNavMeshCarving(bool carve)
        {
            NavMeshObstacle obstacle = GetComponent<NavMeshObstacle>();
            if (obstacle != null)
            {
                obstacle.enabled = carve;
            }
        }
    }
}
