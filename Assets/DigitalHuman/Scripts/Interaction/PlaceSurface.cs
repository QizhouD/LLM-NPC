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
        [SerializeField] private string displayName = "obstacle";
        [SerializeField, Min(0f)] private float surfacePadding = 0.02f;

        private readonly List<GameObject> placedItems = new List<GameObject>(4);

        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;

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
            CleanupDestroyedItems();
            Bounds bounds = GetWorldBounds();
            float halfHeight = Mathf.Max(0.05f, worldScale.y * 0.5f);
            float y = bounds.max.y + halfHeight + surfacePadding;
            for (int i = 0; i < placedItems.Count; i++)
            {
                GameObject placed = placedItems[i];
                if (placed == null)
                {
                    continue;
                }

                Collider placedCollider = placed.GetComponent<Collider>();
                float top = placedCollider != null
                    ? placedCollider.bounds.max.y
                    : placed.transform.position.y + worldScale.y * 0.5f;
                y = Mathf.Max(y, top + halfHeight + surfacePadding);
            }

            if (item != null && !placedItems.Contains(item))
            {
                placedItems.Add(item);
            }

            return new Vector3(bounds.center.x, y, bounds.center.z);
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
