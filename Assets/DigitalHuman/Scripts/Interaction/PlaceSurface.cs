using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman
{
    /// <summary>
    /// A surface the NPC can set a held PickupItem on, such as NavigationObstacle.
    /// Place this on the destination object. Bounds come from Collider or Renderer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaceSurface : MonoBehaviour
    {
        private const float RestingTolerance = 0.06f;

        [SerializeField] private string displayName = "obstacle";
        [SerializeField, Min(0f)] private float surfacePadding = 0.02f;

        private readonly List<GameObject> placedItems = new List<GameObject>(4);

        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;

        private void Start()
        {
            RegisterItemsAlreadyResting();
        }

        public void Configure(string surfaceDisplayName)
        {
            displayName = string.IsNullOrWhiteSpace(surfaceDisplayName)
                ? gameObject.name
                : surfaceDisplayName.Trim();
        }

        public bool Contains(GameObject item)
        {
            return item != null && placedItems.Contains(item);
        }

        public void NotifyRemoved(GameObject item)
        {
            if (item != null)
            {
                placedItems.Remove(item);
            }
        }

        public Vector3 ReservePlacePosition(GameObject item, Vector3 worldScale)
        {
            return ReservePlacePosition(item, worldScale, GetWorldBounds().center);
        }

        /// <summary>
        /// Centre at which an item of <paramref name="worldSize"/> rests on this surface, as
        /// close to <paramref name="approachFrom"/> as fits with the whole item on top, and
        /// above anything already placed under that footprint. Reserves it for the item.
        /// </summary>
        /// <remarks>
        /// Placing nearest the person doing it matters on a surface wider than an arm: the
        /// middle of a 1.8 m obstacle is out of reach from any side.
        /// </remarks>
        public Vector3 ReservePlacePosition(GameObject item, Vector3 worldSize, Vector3 approachFrom)
        {
            CleanupDestroyedItems();
            Bounds bounds = GetWorldBounds();
            float halfHeight = Mathf.Max(0.05f, worldSize.y * 0.5f);
            float x = ClampInside(approachFrom.x, bounds.min.x, bounds.max.x, worldSize.x * 0.5f);
            float z = ClampInside(approachFrom.z, bounds.min.z, bounds.max.z, worldSize.z * 0.5f);
            Bounds footprint = new Bounds(
                new Vector3(x, bounds.max.y, z),
                new Vector3(worldSize.x, 0f, worldSize.z));

            float y = bounds.max.y + halfHeight + surfacePadding;
            for (int i = 0; i < placedItems.Count; i++)
            {
                GameObject placed = placedItems[i];
                if (placed == null || placed == item)
                {
                    continue;
                }

                Collider placedCollider = placed.GetComponent<Collider>();
                Bounds placedBounds = placedCollider != null
                    ? placedCollider.bounds
                    : new Bounds(placed.transform.position, worldSize);
                if (!OverlapsFootprint(footprint, placedBounds))
                {
                    continue;
                }

                y = Mathf.Max(y, placedBounds.max.y + halfHeight + surfacePadding);
            }

            if (item != null && !placedItems.Contains(item))
            {
                placedItems.Add(item);
            }

            return new Vector3(x, y, z);
        }

        public Bounds GetWorldBounds()
        {
            Collider collider = GetComponent<Collider>();
            if (collider != null)
            {
                return collider.bounds;
            }

            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                return renderer.bounds;
            }

            return new Bounds(transform.position, Vector3.one);
        }

        /// <summary>
        /// Counts the pickup items the scene starts with on this surface, including ones
        /// stacked on each other, so the next item set down lands on top of them instead of
        /// inside them. Otherwise only items placed at runtime are known.
        /// </summary>
        private void RegisterItemsAlreadyResting()
        {
            Bounds surface = GetWorldBounds();
            PickupItem[] items = FindObjectsByType<PickupItem>(FindObjectsSortMode.None);
            bool added = true;
            while (added)
            {
                added = false;
                for (int i = 0; i < items.Length; i++)
                {
                    PickupItem item = items[i];
                    if (item == null || item.IsPickedUp || placedItems.Contains(item.gameObject))
                    {
                        continue;
                    }

                    Collider itemCollider = item.GetComponent<Collider>();
                    if (itemCollider == null || !itemCollider.enabled)
                    {
                        continue;
                    }

                    Bounds itemBounds = itemCollider.bounds;
                    if (IsOverFootprint(surface, itemBounds.center) &&
                        RestsOnSurfaceOrStack(surface, itemBounds.min.y))
                    {
                        placedItems.Add(item.gameObject);
                        added = true;
                    }
                }
            }
        }

        private bool RestsOnSurfaceOrStack(Bounds surface, float bottom)
        {
            if (Mathf.Abs(bottom - surface.max.y) <= RestingTolerance)
            {
                return true;
            }

            for (int i = 0; i < placedItems.Count; i++)
            {
                Collider placedCollider = placedItems[i] != null
                    ? placedItems[i].GetComponent<Collider>()
                    : null;
                if (placedCollider != null &&
                    Mathf.Abs(bottom - placedCollider.bounds.max.y) <= RestingTolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOverFootprint(Bounds surface, Vector3 point)
        {
            return point.x >= surface.min.x && point.x <= surface.max.x &&
                   point.z >= surface.min.z && point.z <= surface.max.z;
        }

        private float ClampInside(float value, float min, float max, float halfSize)
        {
            float inset = halfSize + surfacePadding;
            return min + inset > max - inset
                ? (min + max) * 0.5f
                : Mathf.Clamp(value, min + inset, max - inset);
        }

        private static bool OverlapsFootprint(Bounds footprint, Bounds other)
        {
            return footprint.min.x < other.max.x && footprint.max.x > other.min.x &&
                   footprint.min.z < other.max.z && footprint.max.z > other.min.z;
        }

        private void CleanupDestroyedItems()
        {
            for (int i = placedItems.Count - 1; i >= 0; i--)
            {
                if (placedItems[i] == null)
                {
                    placedItems.RemoveAt(i);
                }
            }
        }

        private void OnValidate()
        {
            surfacePadding = Mathf.Max(0f, surfacePadding);
        }
    }
}
