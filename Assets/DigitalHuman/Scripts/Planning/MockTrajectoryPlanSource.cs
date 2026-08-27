using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DigitalHuman.Planning
{
    /// <summary>
    /// Generates a plausible plan locally, in the exact shape the real planner emits.
    /// </summary>
    /// <remarks>
    /// This exists because every plan currently exported by the planner repo has
    /// <c>fallback: true</c> with 30 zeroed points, which cannot drive a demo. The geometry
    /// here is a quadratic Bezier detour around blocking obstacles with a trapezoidal speed
    /// profile. It is deliberately not a model: it is a stand-in that exercises the same
    /// contract so the embodiment layer needs no change when the service comes online.
    /// </remarks>
    public sealed class MockTrajectoryPlanSource : MonoBehaviour, ITrajectoryPlanSource
    {
        [Header("Contract")]
        [Tooltip("Control rate in Hz. The planner contract specifies 10.")]
        [SerializeField, Min(1f)] private float controlHz = 10f;
        [Tooltip("Points per plan. The planner contract specifies 30, i.e. a 3 second horizon.")]
        [SerializeField, Min(2)] private int horizonSteps = 30;

        [Header("Locomotion profile")]
        [SerializeField, Min(0.1f)] private float cruiseSpeed = 1.4f;
        [SerializeField, Min(0.1f)] private float acceleration = 1.8f;
        [SerializeField, Min(0.1f)] private float deceleration = 2.2f;
        [Tooltip("Extra margin added to an obstacle radius when routing around it.")]
        [SerializeField, Min(0f)] private float obstacleClearance = 0.6f;
        [Tooltip("Below this speed the segment is labelled stop or turn instead of walk.")]
        [SerializeField, Min(0f)] private float walkSpeedThreshold = 0.15f;
        [Tooltip("Above this yaw rate a near-stationary segment is labelled turn.")]
        [SerializeField, Min(0f)] private float turnRateThreshold = 0.35f;

        private int planCounter;

        public string SourceName => "mock:bezier-detour";

        public float ControlHz => controlHz;
        public int HorizonSteps => horizonSteps;

        public Task<TrajectoryPlan> RequestPlanAsync(
            PlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null)
            {
                return Task.FromException<TrajectoryPlan>(new ArgumentNullException(nameof(request)));
            }

            try
            {
                return Task.FromResult(Build(request));
            }
            catch (Exception exception)
            {
                return Task.FromException<TrajectoryPlan>(exception);
            }
        }

        private TrajectoryPlan Build(PlanRequest request)
        {
            float dt = 1f / controlHz;
            Vector3 start = request.CurrentPosition;
            Vector3 goal = request.Goal.TargetPosition;
            goal.y = start.y;

            Vector3 toGoal = goal - start;
            toGoal.y = 0f;
            float directDistance = toGoal.magnitude;

            // Stop short of the goal by the requested standoff, e.g. conversation distance.
            float travelDistance = Mathf.Max(0f, directDistance - request.Goal.StopDistance);
            Vector3 approachDirection = directDistance > 1e-4f
                ? toGoal / directDistance
                : YawToDirection(request.CurrentYaw);
            Vector3 stopPoint = start + approachDirection * travelDistance;

            Vector3 control = ComputeDetourControl(start, stopPoint, request.Obstacles);
            List<Vector3> path = SamplePath(start, control, stopPoint);
            List<float> cumulative = BuildArcLengthTable(path);
            float pathLength = cumulative[cumulative.Count - 1];

            List<TrajectoryPoint> points = new List<TrajectoryPoint>(horizonSteps);
            float travelled = 0f;
            float previousYaw = request.CurrentYaw;

            for (int step = 0; step < horizonSteps; step++)
            {
                float time = step * dt;
                float speed = SpeedAt(travelled, pathLength);
                Vector3 position = PositionAtDistance(path, cumulative, travelled);
                Vector3 tangent = TangentAtDistance(path, cumulative, travelled, approachDirection);

                float yaw = pathLength > 1e-4f && speed > 1e-3f
                    ? DirectionToYaw(tangent)
                    : previousYaw;
                float angularVelocity = ShortestAngleDelta(previousYaw, yaw) / dt;

                points.Add(new TrajectoryPoint(
                    time,
                    position,
                    yaw,
                    tangent * speed,
                    angularVelocity,
                    speed));

                previousYaw = yaw;
                travelled = Mathf.Min(pathLength, travelled + speed * dt);
            }

            List<MotionSegment> segments = BuildSegments(points, request.Goal.Behavior, dt);
            planCounter++;

            return TrajectoryPlan.FromPoints(
                $"mock-{planCounter:D4}",
                controlHz,
                points,
                segments,
                ScorePlan(pathLength, request.Obstacles, points));
        }

        private Vector3 ComputeDetourControl(
            Vector3 start,
            Vector3 end,
            IReadOnlyList<PlanObstacle> obstacles)
        {
            Vector3 midpoint = (start + end) * 0.5f;
            Vector3 along = end - start;
            along.y = 0f;
            float length = along.magnitude;
            if (length < 1e-4f || obstacles == null || obstacles.Count == 0)
            {
                return midpoint;
            }

            Vector3 direction = along / length;
            Vector3 normal = new Vector3(-direction.z, 0f, direction.x);

            float strongestPush = 0f;
            float pushSign = 1f;
            for (int i = 0; i < obstacles.Count; i++)
            {
                PlanObstacle obstacle = obstacles[i];
                Vector3 toObstacle = obstacle.Center - start;
                toObstacle.y = 0f;
                float alongDistance = Vector3.Dot(toObstacle, direction);
                if (alongDistance < 0f || alongDistance > length)
                {
                    continue;
                }

                float lateral = Vector3.Dot(toObstacle, normal);
                float required = obstacle.Radius + obstacleClearance;
                if (Mathf.Abs(lateral) >= required)
                {
                    continue;
                }

                // Push the control point to the side the obstacle is not on. A Bezier
                // control point pulls the curve roughly half its offset, hence the doubling.
                float push = (required - Mathf.Abs(lateral)) * 2f;
                if (push > strongestPush)
                {
                    strongestPush = push;
                    pushSign = lateral >= 0f ? -1f : 1f;
                }
            }

            return midpoint + normal * (pushSign * strongestPush);
        }

        private List<Vector3> SamplePath(Vector3 start, Vector3 control, Vector3 end)
        {
            const int resolution = 64;
            List<Vector3> path = new List<Vector3>(resolution + 1);
            for (int i = 0; i <= resolution; i++)
            {
                float u = i / (float)resolution;
                float inverse = 1f - u;
                path.Add(
                    inverse * inverse * start +
                    2f * inverse * u * control +
                    u * u * end);
            }

            return path;
        }

        private static List<float> BuildArcLengthTable(List<Vector3> path)
        {
            List<float> cumulative = new List<float>(path.Count) { 0f };
            for (int i = 1; i < path.Count; i++)
            {
                cumulative.Add(cumulative[i - 1] + Vector3.Distance(path[i - 1], path[i]));
            }

            return cumulative;
        }

        private float SpeedAt(float travelled, float pathLength)
        {
            if (pathLength <= 1e-4f)
            {
                return 0f;
            }

            float remaining = Mathf.Max(0f, pathLength - travelled);
            float accelerationLimit = Mathf.Sqrt(2f * acceleration * Mathf.Max(0f, travelled));
            float decelerationLimit = Mathf.Sqrt(2f * deceleration * remaining);
            // A standing start would otherwise sample exactly zero and never move.
            float floorSpeed = travelled <= 1e-4f ? cruiseSpeed * 0.25f : 0f;
            return Mathf.Max(
                Mathf.Min(cruiseSpeed, Mathf.Min(accelerationLimit, decelerationLimit)),
                Mathf.Min(floorSpeed, decelerationLimit));
        }

        private static Vector3 PositionAtDistance(
            List<Vector3> path,
            List<float> cumulative,
            float distance)
        {
            if (distance <= 0f)
            {
                return path[0];
            }

            float total = cumulative[cumulative.Count - 1];
            if (distance >= total)
            {
                return path[path.Count - 1];
            }

            for (int i = 1; i < cumulative.Count; i++)
            {
                if (cumulative[i] < distance)
                {
                    continue;
                }

                float span = cumulative[i] - cumulative[i - 1];
                float alpha = span <= 1e-6f ? 0f : (distance - cumulative[i - 1]) / span;
                return Vector3.Lerp(path[i - 1], path[i], alpha);
            }

            return path[path.Count - 1];
        }

        private static Vector3 TangentAtDistance(
            List<Vector3> path,
            List<float> cumulative,
            float distance,
            Vector3 fallbackDirection)
        {
            float total = cumulative[cumulative.Count - 1];
            if (total <= 1e-4f)
            {
                return fallbackDirection;
            }

            const float epsilon = 0.05f;
            Vector3 ahead = PositionAtDistance(
                path, cumulative, Mathf.Min(total, distance + epsilon));
            Vector3 behind = PositionAtDistance(
                path, cumulative, Mathf.Max(0f, distance - epsilon));
            Vector3 tangent = ahead - behind;
            tangent.y = 0f;
            return tangent.sqrMagnitude < 1e-8f ? fallbackDirection : tangent.normalized;
        }

        private List<MotionSegment> BuildSegments(
            List<TrajectoryPoint> points,
            MotionIntent goalBehavior,
            float dt)
        {
            List<MotionSegment> segments = new List<MotionSegment>();
            MotionIntent runIntent = ClassifyIntent(points[0]);
            float runStart = points[0].Time;

            for (int i = 1; i < points.Count; i++)
            {
                MotionIntent intent = ClassifyIntent(points[i]);
                if (intent == runIntent)
                {
                    continue;
                }

                segments.Add(new MotionSegment(
                    runStart, points[i].Time, runIntent, PromptFor(runIntent)));
                runIntent = intent;
                runStart = points[i].Time;
            }

            float planEnd = points[points.Count - 1].Time + dt;

            // A goal of "wave" means the greeting happens once the character has arrived,
            // so it replaces the trailing stationary run rather than being appended after it.
            if (goalBehavior == MotionIntent.Wave && runIntent != MotionIntent.Walk)
            {
                segments.Add(new MotionSegment(
                    runStart, planEnd, MotionIntent.Wave, PromptFor(MotionIntent.Wave)));
                return segments;
            }

            segments.Add(new MotionSegment(runStart, planEnd, runIntent, PromptFor(runIntent)));
            return segments;
        }

        private MotionIntent ClassifyIntent(TrajectoryPoint point)
        {
            if (point.Speed > walkSpeedThreshold)
            {
                return MotionIntent.Walk;
            }

            return Mathf.Abs(point.AngularVelocity) > turnRateThreshold
                ? MotionIntent.Turn
                : MotionIntent.Stop;
        }

        private static string PromptFor(MotionIntent intent)
        {
            switch (intent)
            {
                case MotionIntent.Walk:
                    return "walk forward at a natural pace";
                case MotionIntent.Turn:
                    return "turn in place towards the target";
                case MotionIntent.Stop:
                    return "come to a settled stop";
                case MotionIntent.Wave:
                    return "raise one hand and wave in greeting";
                default:
                    return "stand idle";
            }
        }

        /// <summary>
        /// Mimics the planner's candidate score: shorter paths and larger obstacle clearance
        /// score lower, and lower is better.
        /// </summary>
        private float ScorePlan(
            float pathLength,
            IReadOnlyList<PlanObstacle> obstacles,
            List<TrajectoryPoint> points)
        {
            float score = pathLength;
            if (obstacles == null || obstacles.Count == 0)
            {
                return score;
            }

            float minimumGap = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                for (int o = 0; o < obstacles.Count; o++)
                {
                    Vector3 delta = points[i].Position - obstacles[o].Center;
                    delta.y = 0f;
                    minimumGap = Mathf.Min(minimumGap, delta.magnitude - obstacles[o].Radius);
                }
            }

            return score + Mathf.Max(0f, obstacleClearance - minimumGap) * 10f;
        }

        private static Vector3 YawToDirection(float yawRadians)
        {
            return new Vector3(Mathf.Sin(yawRadians), 0f, Mathf.Cos(yawRadians));
        }

        /// <summary>
        /// Unity's Y euler angle measures clockwise from +Z when viewed from above, which is
        /// <c>atan2(x, z)</c> rather than the mathematical <c>atan2(z, x)</c>.
        /// </summary>
        private static float DirectionToYaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z);
        }

        private static float ShortestAngleDelta(float from, float to)
        {
            float difference = Mathf.Repeat(to - from, Mathf.PI * 2f);
            return difference > Mathf.PI ? difference - Mathf.PI * 2f : difference;
        }

        private void OnValidate()
        {
            controlHz = Mathf.Max(1f, controlHz);
            horizonSteps = Mathf.Max(2, horizonSteps);
        }
    }
}
