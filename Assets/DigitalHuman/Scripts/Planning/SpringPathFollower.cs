using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>Tuning for <see cref="SpringPathFollower"/>. Defaults match the planner repo.</summary>
    [Serializable]
    public sealed class SpringFollowerSettings
    {
        [Tooltip("Seconds for the simulated velocity to cover half the gap to the desired velocity.")]
        [Min(0.01f)] public float velocityHalflife = 0.27f;
        [Tooltip("Seconds for the facing to cover half the gap to the direction of travel.")]
        [Min(0.01f)] public float facingHalflife = 0.2f;
        [Tooltip("Braking used when approaching a goal, in metres per second squared.")]
        [Min(0.1f)] public float deceleration = 3f;
        [Tooltip("The steering target sits this many seconds of travel ahead on the path.")]
        [Min(0.05f)] public float lookaheadSeconds = 0.5f;
        [Min(0.05f)] public float minLookahead = 0.4f;
        [Tooltip("Time constant of the final approach to the goal.")]
        [Min(0.05f)] public float arrivalSeconds = 0.6f;
        [Tooltip("Integration steps per control step.")]
        [Min(1)] public int substeps = 3;
    }

    /// <summary>
    /// Classical path follower: pure-pursuit steering smoothed by a critically damped
    /// spring on velocity. It is a line-for-line port of <c>spring_follow</c> in
    /// <c>src/gtp/rootmotion/baselines.py</c>, so the baseline measured offline in Python
    /// is the same controller that runs here, and it is what the HTTP source falls back
    /// to when the learned planner is unreachable or its plan leaves the NavMesh.
    /// </summary>
    public static class SpringPathFollower
    {
        private const float Ln2 = 0.69314718056f;
        private const float FacingSpeedThreshold = 0.3f;

        /// <param name="path">World-space polyline whose first vertex is the current position.</param>
        /// <param name="endsAtGoal">False when the path continues beyond its last vertex.</param>
        /// <param name="arrivalYaw">Heading to settle into once stationary, in radians.</param>
        /// <returns><paramref name="steps"/> + 1 points; the first is the current pose at t = 0.</returns>
        public static List<TrajectoryPoint> Simulate(
            IReadOnlyList<Vector3> path,
            bool endsAtGoal,
            Vector3 startVelocity,
            float startYaw,
            float targetSpeed,
            SpringFollowerSettings settings,
            int steps = 30,
            float controlHz = 10f,
            float? arrivalYaw = null)
        {
            if (path == null || path.Count == 0)
            {
                throw new ArgumentException("Path needs at least the current position.", nameof(path));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (steps < 1 || controlHz <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(steps));
            }

            List<Vector3> polyline = new List<Vector3>(path.Count + 1);
            for (int i = 0; i < path.Count; i++)
            {
                polyline.Add(path[i]);
            }

            if (!endsAtGoal)
            {
                // The path continues beyond what was supplied: extend it straight ahead so
                // the look-ahead point never stalls at the last vertex.
                polyline.Add(polyline[polyline.Count - 1] + PathGeometry.LastDirection(polyline) * 50f);
            }

            List<float> cumulative = new List<float>(polyline.Count);
            PathGeometry.BuildArcLengths(polyline, cumulative);
            float total = cumulative[cumulative.Count - 1];

            int substeps = Mathf.Max(1, settings.substeps);
            float dt = 1f / (controlHz * substeps);
            float damping = 4f * Ln2 / Mathf.Max(settings.velocityHalflife, 1e-5f) / 2f;
            float decay = Mathf.Exp(-damping * dt);
            float facingBlend = 1f - Mathf.Exp(-Ln2 * dt / Mathf.Max(settings.facingHalflife, 1e-5f));

            float height = polyline[0].y;
            Vector2 position = new Vector2(polyline[0].x, polyline[0].z);
            Vector2 velocity = new Vector2(startVelocity.x, startVelocity.z);
            Vector2 acceleration = Vector2.zero;
            float yaw = startYaw;
            float progress = 0f;

            List<TrajectoryPoint> points = new List<TrajectoryPoint>(steps + 1);
            Vector2 previousPosition = position;
            float previousYaw = yaw;

            for (int step = 0; step < steps; step++)
            {
                for (int sub = 0; sub < substeps; sub++)
                {
                    float speed = velocity.magnitude;
                    Vector3 here = new Vector3(position.x, height, position.y);
                    progress = PathGeometry.Project(polyline, cumulative, here, progress, 2f + speed);
                    float lookahead = Mathf.Max(settings.minLookahead, settings.lookaheadSeconds * speed);
                    Vector3 target = PathGeometry.PointAt(polyline, cumulative, progress + lookahead);
                    Vector2 offset = new Vector2(target.x - position.x, target.z - position.y);
                    float distance = offset.magnitude;
                    Vector2 direction = distance > 1e-6f ? offset / distance : Vector2.zero;

                    float desired = targetSpeed;
                    bool arriving = false;
                    if (endsAtGoal)
                    {
                        float remaining = Mathf.Max(0f, total - progress);
                        if (remaining <= lookahead)
                        {
                            // Arrival: the target is the goal itself. Slowing in proportion to
                            // the straight-line distance also pulls back spring overshoot.
                            desired = Mathf.Min(desired, distance / settings.arrivalSeconds);
                            arriving = true;
                        }
                        else
                        {
                            desired = Mathf.Min(desired, Mathf.Sqrt(2f * settings.deceleration * remaining));
                        }
                    }

                    Vector2 goalVelocity = direction * desired;
                    Vector2 j0 = velocity - goalVelocity;
                    Vector2 j1 = acceleration + j0 * damping;
                    position += decay * (-j1 / (damping * damping) + (-j0 - j1 * dt) / damping)
                                + j1 / (damping * damping) + j0 / damping + goalVelocity * dt;
                    velocity = decay * (j0 + j1 * dt) + goalVelocity;
                    acceleration = decay * (acceleration - j1 * damping * dt);

                    if (velocity.magnitude > FacingSpeedThreshold)
                    {
                        yaw += ShortestAngle(yaw, Mathf.Atan2(velocity.x, velocity.y)) * facingBlend;
                    }
                    else if (arriving && arrivalYaw.HasValue)
                    {
                        yaw += ShortestAngle(yaw, arrivalYaw.Value) * facingBlend;
                    }
                }

                Vector2 moved = (position - previousPosition) * controlHz;
                float turnRate = ShortestAngle(previousYaw, yaw) * controlHz;
                if (step == 0)
                {
                    // The pose at t = 0 inherits the first step's velocity, as the service does.
                    points.Add(new TrajectoryPoint(
                        0f,
                        new Vector3(previousPosition.x, height, previousPosition.y),
                        previousYaw,
                        new Vector3(moved.x, 0f, moved.y),
                        turnRate,
                        moved.magnitude));
                }

                points.Add(new TrajectoryPoint(
                    (step + 1) / controlHz,
                    new Vector3(position.x, height, position.y),
                    yaw,
                    new Vector3(moved.x, 0f, moved.y),
                    turnRate,
                    moved.magnitude));
                previousPosition = position;
                previousYaw = yaw;
            }

            return points;
        }

        /// <summary>Signed shortest rotation from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static float ShortestAngle(float from, float to)
        {
            float difference = Mathf.Repeat(to - from, Mathf.PI * 2f);
            return difference > Mathf.PI ? difference - Mathf.PI * 2f : difference;
        }
    }
}
