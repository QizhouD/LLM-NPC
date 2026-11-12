using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Ground-plane polyline helpers. Every distance ignores the vertical axis, matching
    /// the planner, which works in the XZ plane.
    /// </summary>
    public static class PathGeometry
    {
        public static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Fills <paramref name="cumulative"/> with arclength at each vertex, starting at zero.</summary>
        public static void BuildArcLengths(IReadOnlyList<Vector3> path, List<float> cumulative)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            cumulative.Clear();
            float total = 0f;
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0)
                {
                    total += PlanarDistance(path[i - 1], path[i]);
                }

                cumulative.Add(total);
            }
        }

        public static float Length(IReadOnlyList<Vector3> path)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                total += PlanarDistance(path[i - 1], path[i]);
            }

            return total;
        }

        /// <summary>Position at <paramref name="distance"/> along the path, clamped to its ends.</summary>
        public static Vector3 PointAt(
            IReadOnlyList<Vector3> path,
            IReadOnlyList<float> cumulative,
            float distance)
        {
            int last = path.Count - 1;
            if (last <= 0 || distance <= 0f)
            {
                return path[0];
            }

            if (distance >= cumulative[last])
            {
                return path[last];
            }

            for (int i = 1; i <= last; i++)
            {
                if (cumulative[i] < distance)
                {
                    continue;
                }

                float span = cumulative[i] - cumulative[i - 1];
                float alpha = span <= 1e-6f ? 0f : (distance - cumulative[i - 1]) / span;
                return Vector3.Lerp(path[i - 1], path[i], alpha);
            }

            return path[last];
        }

        /// <summary>
        /// Arclength of the point on the path closest to <paramref name="query"/>, searched
        /// only between <paramref name="start"/> and <paramref name="start"/> +
        /// <paramref name="window"/> so progress never jumps backwards or across a loop.
        /// </summary>
        public static float Project(
            IReadOnlyList<Vector3> path,
            IReadOnlyList<float> cumulative,
            Vector3 query,
            float start,
            float window)
        {
            int last = path.Count - 1;
            if (last <= 0)
            {
                return 0f;
            }

            float limit = Mathf.Min(cumulative[last], start + window);
            float bestDistance = float.MaxValue;
            float bestArcLength = start;
            for (int i = 0; i < last; i++)
            {
                if (cumulative[i + 1] < start || cumulative[i] > limit)
                {
                    continue;
                }

                float sx = path[i + 1].x - path[i].x;
                float sz = path[i + 1].z - path[i].z;
                float lengthSquared = sx * sx + sz * sz;
                float ratio = lengthSquared < 1e-12f
                    ? 0f
                    : Mathf.Clamp01(
                        ((query.x - path[i].x) * sx + (query.z - path[i].z) * sz) / lengthSquared);
                float along = cumulative[i] + ratio * (cumulative[i + 1] - cumulative[i]);
                along = Mathf.Clamp(along, start, limit);
                float distance = PlanarDistance(query, PointAt(path, cumulative, along));
                if (distance < bestDistance - 1e-6f)
                {
                    bestDistance = distance;
                    bestArcLength = along;
                }
            }

            return bestArcLength;
        }

        /// <summary>Planar distance from <paramref name="query"/> to the nearest point of the path.</summary>
        public static float DistanceToPath(IReadOnlyList<Vector3> path, Vector3 query)
        {
            if (path == null || path.Count == 0)
            {
                throw new ArgumentException("Path is empty.", nameof(path));
            }

            float best = PlanarDistance(query, path[0]);
            for (int i = 0; i < path.Count - 1; i++)
            {
                float sx = path[i + 1].x - path[i].x;
                float sz = path[i + 1].z - path[i].z;
                float lengthSquared = sx * sx + sz * sz;
                float ratio = lengthSquared < 1e-12f
                    ? 0f
                    : Mathf.Clamp01(
                        ((query.x - path[i].x) * sx + (query.z - path[i].z) * sz) / lengthSquared);
                Vector3 closest = Vector3.Lerp(path[i], path[i + 1], ratio);
                best = Mathf.Min(best, PlanarDistance(query, closest));
            }

            return best;
        }

        /// <summary>
        /// Shortens the path by <paramref name="distance"/> from its end, so a character
        /// stops at conversation distance instead of on top of its target.
        /// </summary>
        public static void TrimEnd(List<Vector3> path, float distance)
        {
            if (path == null || path.Count < 2 || distance <= 0f)
            {
                return;
            }

            float remaining = distance;
            while (path.Count >= 2)
            {
                Vector3 end = path[path.Count - 1];
                Vector3 previous = path[path.Count - 2];
                float segment = PlanarDistance(previous, end);
                if (segment > remaining)
                {
                    path[path.Count - 1] = Vector3.Lerp(end, previous, remaining / segment);
                    return;
                }

                remaining -= segment;
                path.RemoveAt(path.Count - 1);
            }
        }

        /// <summary>Unit direction of the last non-degenerate segment, or +Z when there is none.</summary>
        public static Vector3 LastDirection(IReadOnlyList<Vector3> path)
        {
            for (int i = path.Count - 1; i > 0; i--)
            {
                Vector3 segment = path[i] - path[i - 1];
                segment.y = 0f;
                if (segment.sqrMagnitude > 1e-12f)
                {
                    return segment.normalized;
                }
            }

            return Vector3.forward;
        }
    }
}
